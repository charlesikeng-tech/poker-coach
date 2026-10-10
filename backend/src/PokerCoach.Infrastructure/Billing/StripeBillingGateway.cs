using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Infrastructure.Billing;

/// <summary>Configuration section "Billing:Stripe". Keys come from user-secrets or a secret store, never a file.</summary>
public sealed class StripeOptions
{
    public const string SectionName = "Billing:Stripe";

    /// <summary>Unset: billing is off and every account has Pro (ADR-0014).</summary>
    public string? SecretKey { get; set; }

    /// <summary>The webhook endpoint's signing secret (whsec_…).</summary>
    public string? WebhookSecret { get; set; }

    public StripePrices Prices { get; set; } = new();

    public Uri BaseUrl { get; set; } = new("https://api.stripe.com/");

    /// <summary>Webhooks signed longer ago than this are refused (replay protection).</summary>
    public TimeSpan WebhookTolerance { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class StripePrices
{
    /// <summary>Price id of Pro billed monthly (price_…).</summary>
    public string? Monthly { get; set; }

    public string? Yearly { get; set; }
}

/// <summary>
/// Stripe over plain HTTP (ADR-0014): five endpoints, form-encoded, plus webhook signature checks. Errors are
/// logged with Stripe's error type and message (never card data) and thrown as <see cref="BillingGatewayException"/>.
/// </summary>
internal sealed partial class StripeBillingGateway(
    HttpClient http,
    IOptions<StripeOptions> options,
    TimeProvider time,
    ILogger<StripeBillingGateway> logger) : IBillingGateway
{
    private StripeOptions Settings => options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.SecretKey);

    public async Task<string> CreateCustomerAsync(Guid userId, string? email, CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>> { new("metadata[user_id]", userId.ToString()) };
        if (!string.IsNullOrWhiteSpace(email))
        {
            form.Add(new("email", email));
        }

        // Idempotency key: a retried request after a timeout does not create a second customer.
        using var json = await SendAsync(HttpMethod.Post, "v1/customers", form, $"customer-{userId}", cancellationToken);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var price = request.Interval == BillingInterval.Year ? Settings.Prices.Yearly : Settings.Prices.Monthly;
        if (string.IsNullOrWhiteSpace(price))
        {
            throw new BillingGatewayException($"No Stripe price configured for {request.Interval}.");
        }

        using var json = await SendAsync(
            HttpMethod.Post,
            "v1/checkout/sessions",
            [
                new("mode", "subscription"),
                new("customer", request.CustomerId),
                new("client_reference_id", request.UserId.ToString()),
                new("line_items[0][price]", price),
                new("line_items[0][quantity]", "1"),
                new("allow_promotion_codes", "true"),
                new("locale", request.Language),
                new("subscription_data[metadata][user_id]", request.UserId.ToString()),
                new("success_url", request.SuccessUrl.ToString()),
                new("cancel_url", request.CancelUrl.ToString()),
            ],
            idempotencyKey: null,
            cancellationToken);
        return new Uri(json.RootElement.GetProperty("url").GetString()!);
    }

    public async Task<Uri> CreatePortalAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(returnUrl);
        using var json = await SendAsync(
            HttpMethod.Post,
            "v1/billing_portal/sessions",
            [new("customer", customerId), new("return_url", returnUrl.ToString())],
            idempotencyKey: null,
            cancellationToken);
        return new Uri(json.RootElement.GetProperty("url").GetString()!);
    }

    public async Task<IReadOnlyList<SubscriptionState>> ListSubscriptionsAsync(string customerId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(
            HttpMethod.Get,
            $"v1/subscriptions?customer={Uri.EscapeDataString(customerId)}&status=all&limit=20",
            form: null,
            idempotencyKey: null,
            cancellationToken);
        return [.. json.RootElement.GetProperty("data").EnumerateArray().Select(StateOf).OfType<SubscriptionState>()];
    }

    public async Task DeleteCustomerAsync(string customerId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete, $"v1/customers/{Uri.EscapeDataString(customerId)}", form: null, idempotencyKey: null, cancellationToken);
        }
        catch (BillingGatewayException exception) when (exception.Message.Contains("resource_missing", StringComparison.Ordinal))
        {
            // Already deleted (from the dashboard, or a retried deletion): the goal is reached.
        }
    }

    public BillingEvent? ReadWebhook(string payload, string? signatureHeader)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(Settings.WebhookSecret) || !StripeSignature.IsValid(payload, signatureHeader, Settings.WebhookSecret, time.GetUtcNow(), Settings.WebhookTolerance))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            var data = root.GetProperty("data").GetProperty("object");
            var customer = data.TryGetProperty("object", out var kind) && kind.GetString() == "customer"
                ? data.GetProperty("id").GetString()
                : data.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            return new BillingEvent(root.GetProperty("id").GetString()!, root.GetProperty("type").GetString()!, customer);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Signed by Stripe but not the shape we expect: nothing to act on.
            LogUnreadableEvent(logger, exception.Message);
            return null;
        }
    }

    /// <summary>Null for an object we cannot read: it is left out rather than guessed.</summary>
    internal static SubscriptionState? StateOf(JsonElement subscription)
    {
        var status = subscription.GetProperty("status").GetString() switch
        {
            "incomplete" => SubscriptionStatus.Incomplete,
            "incomplete_expired" => SubscriptionStatus.IncompleteExpired,
            "trialing" => SubscriptionStatus.Trialing,
            "active" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" => SubscriptionStatus.Canceled,
            "unpaid" => SubscriptionStatus.Unpaid,
            "paused" => SubscriptionStatus.Paused,
            _ => (SubscriptionStatus?)null,
        };
        if (status is null)
        {
            return null;
        }

        // Recent API versions moved the period to the subscription items; older ones keep it on the subscription.
        var item = subscription.TryGetProperty("items", out var items) && items.GetProperty("data").GetArrayLength() > 0
            ? items.GetProperty("data")[0]
            : (JsonElement?)null;
        var periodEnd = UnixTime(item, "current_period_end") ?? UnixTime(subscription, "current_period_end");
        var interval = item is { } i && i.TryGetProperty("price", out var price) && price.TryGetProperty("recurring", out var recurring)
            && recurring.ValueKind == JsonValueKind.Object
            ? recurring.GetProperty("interval").GetString() switch
            {
                "month" => BillingInterval.Month,
                "year" => BillingInterval.Year,
                _ => (BillingInterval?)null,
            }
            : null;
        return new SubscriptionState(
            subscription.GetProperty("id").GetString()!,
            status.Value,
            interval,
            periodEnd,
            subscription.TryGetProperty("cancel_at_period_end", out var cancel) && cancel.ValueKind == JsonValueKind.True);
    }

    private static DateTimeOffset? UnixTime(JsonElement? element, string property) =>
        element is { } e && e.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(value.GetInt64())
            : null;

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>>? form,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(Settings.BaseUrl, path));
        request.Headers.Authorization = new("Bearer", Settings.SecretKey);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogFailure(logger, method.Method, path, 0, exception.Message);
            throw new BillingGatewayException("Stripe could not be reached.", exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var reason = ErrorOf(body);
                LogFailure(logger, method.Method, path.Split('?')[0], (int)response.StatusCode, reason);
                throw new BillingGatewayException($"Stripe answered {(int)response.StatusCode}: {reason}");
            }

            return JsonDocument.Parse(body);
        }
    }

    private static string ErrorOf(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var error = json.RootElement.GetProperty("error");
            var code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
            return string.Join(" ", new[] { code, message }.Where(s => !string.IsNullOrEmpty(s)));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Stripe {Method} {Path} failed (HTTP {Status}): {Reason}")]
    private static partial void LogFailure(ILogger logger, string method, string path, int status, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stripe webhook signed but unreadable: {Reason}")]
    private static partial void LogUnreadableEvent(ILogger logger, string reason);
}

/// <summary>
/// Stripe's webhook signature: header <c>t=timestamp,v1=hex(HMAC-SHA256(secret, "timestamp.payload"))</c>,
/// possibly several v1 values (secret rotation). Compared in constant time, refused when too old.
/// </summary>
internal static class StripeSignature
{
    public static bool IsValid(string payload, string? header, string secret, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0] == "t" && long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var t))
            {
                timestamp = t;
            }
            else if (pair[0] == "v1")
            {
                signatures.Add(pair[1]);
            }
        }

        if (timestamp is null || signatures.Count == 0 || (now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value)).Duration() > tolerance)
        {
            return false;
        }

        var expected = Sign(payload, timestamp.Value, secret);
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), Encoding.ASCII.GetBytes(expected)));
    }

    public static string Sign(string payload, long timestamp, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{payload}")));
}
