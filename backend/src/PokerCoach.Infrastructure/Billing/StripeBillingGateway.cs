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

    /// <summary>Price ids per paid offer (ADR-0016).</summary>
    public StripePlanPrices Prices { get; set; } = new();

    /// <summary>Price ids for one-time credit top-up packs.</summary>
    public StripeTopUpPrices TopUpPacks { get; set; } = new();

    public Uri BaseUrl { get; set; } = new("https://api.stripe.com/");

    /// <summary>Webhooks signed longer ago than this are refused (replay protection).</summary>
    public TimeSpan WebhookTolerance { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class StripePrices
{
    /// <summary>Price id billed monthly (price_…).</summary>
    public string? Monthly { get; set; }

    public string? Yearly { get; set; }
}

public sealed class StripeTopUpPrices
{
    /// <summary>Price ID for the "Recharge" pack: 50 credits, 4,99 €.</summary>
    public string? Recharge { get; set; }

    /// <summary>Price ID for the "Session" pack: 120 credits, 9,99 €.</summary>
    public string? Session { get; set; }

    /// <summary>Price ID for the "Series" pack: 250 credits, 19,99 €.</summary>
    public string? Series { get; set; }

    /// <summary>Credits for a given price ID; null if not a top-up price.</summary>
    public int? CreditsOf(string? priceId) =>
        !string.IsNullOrEmpty(priceId) && priceId == Recharge ? 50
        : !string.IsNullOrEmpty(priceId) && priceId == Session ? 120
        : !string.IsNullOrEmpty(priceId) && priceId == Series ? 250
        : null;

    public string? For(int credits) => credits switch
    {
        50 => Recharge,
        120 => Session,
        250 => Series,
        _ => null,
    };
}

public sealed class StripePlanPrices
{
    public StripePrices Pro { get; set; } = new();

    public StripePrices Elite { get; set; } = new();

    public StripePrices Ultimate { get; set; } = new();

    public string? For(Plan plan, BillingInterval interval)
    {
        var prices = plan switch
        {
            Plan.Ultimate => Ultimate,
            Plan.Elite => Elite,
            Plan.Pro => Pro,
            _ => null,
        };
        return interval == BillingInterval.Year ? prices?.Yearly : prices?.Monthly;
    }

    /// <summary>The offer a price belongs to; null for a price not configured here.</summary>
    public Plan? TierOf(string? priceId) =>
        string.IsNullOrEmpty(priceId) ? null
        : priceId == Ultimate.Monthly || priceId == Ultimate.Yearly ? Plan.Ultimate
        : priceId == Elite.Monthly || priceId == Elite.Yearly ? Plan.Elite
        : priceId == Pro.Monthly || priceId == Pro.Yearly ? Plan.Pro
        : null;
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
        var price = Settings.Prices.For(request.Plan, request.Interval);
        if (string.IsNullOrWhiteSpace(price))
        {
            throw new BillingGatewayException($"No Stripe price configured for {request.Plan} billed {request.Interval}.");
        }

        var form = new List<KeyValuePair<string, string>>
        {
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
        };
        if (request.TrialDays is { } trialDays)
        {
            form.Add(new("subscription_data[trial_period_days]", trialDays.ToString(CultureInfo.InvariantCulture)));
        }

        using var json = await SendAsync(
            HttpMethod.Post,
            "v1/checkout/sessions",
            form,
            idempotencyKey: null,
            cancellationToken);
        return new Uri(json.RootElement.GetProperty("url").GetString()!);
    }

    public async Task<Uri> CreatePlanChangeAsync(string customerId, string subscriptionId, Plan plan, BillingInterval interval, Uri returnUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(returnUrl);
        var price = Settings.Prices.For(plan, interval);
        if (string.IsNullOrWhiteSpace(price))
        {
            throw new BillingGatewayException($"No Stripe price configured for {plan} billed {interval}.");
        }

        // The confirmation flow replaces an existing subscription item: read which one.
        string itemId;
        using (var subscription = await SendAsync(HttpMethod.Get, $"v1/subscriptions/{Uri.EscapeDataString(subscriptionId)}", null, idempotencyKey: null, cancellationToken))
        {
            var items = subscription.RootElement.GetProperty("items").GetProperty("data");
            if (items.GetArrayLength() == 0)
            {
                throw new BillingGatewayException($"Subscription {subscriptionId} has no item to change.");
            }

            itemId = items[0].GetProperty("id").GetString()!;
        }

        using var json = await SendAsync(
            HttpMethod.Post,
            "v1/billing_portal/sessions",
            [
                new("customer", customerId),
                new("return_url", returnUrl.ToString()),
                new("flow_data[type]", "subscription_update_confirm"),
                new("flow_data[subscription_update_confirm][subscription]", subscriptionId),
                new("flow_data[subscription_update_confirm][items][0][id]", itemId),
                new("flow_data[subscription_update_confirm][items][0][price]", price),
                new("flow_data[subscription_update_confirm][items][0][quantity]", "1"),
                new("flow_data[after_completion][type]", "redirect"),
                new("flow_data[after_completion][redirect][return_url]", returnUrl.ToString()),
            ],
            idempotencyKey: null,
            cancellationToken);
        return new Uri(json.RootElement.GetProperty("url").GetString()!);
    }

    public async Task<Uri> CreateTopUpCheckoutAsync(TopUpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var price = Settings.TopUpPacks.For(request.Credits);
        if (string.IsNullOrWhiteSpace(price))
        {
            throw new BillingGatewayException($"No Stripe price configured for top-up pack of {request.Credits} credits.");
        }

        using var json = await SendAsync(
            HttpMethod.Post,
            "v1/checkout/sessions",
            [
                new("mode", "payment"),
                new("client_reference_id", request.UserId.ToString()),
                new("metadata[user_id]", request.UserId.ToString()),
                new("metadata[credits]", request.Credits.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("line_items[0][price]", price),
                new("line_items[0][quantity]", "1"),
                new("locale", request.Language),
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
        return [.. json.RootElement.GetProperty("data").EnumerateArray().Select(s => StateOf(s, Settings.Prices.TierOf)).OfType<SubscriptionState>()];
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
            var eventType = root.GetProperty("type").GetString()!;
            var data = root.GetProperty("data").GetProperty("object");
            var customer = data.TryGetProperty("object", out var kind) && kind.GetString() == "customer"
                ? data.GetProperty("id").GetString()
                : data.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;

            // One-time payment: extract our metadata for the credit top-up flow.
            Guid? creditUserId = null;
            int? credits = null;
            if (eventType == "checkout.session.completed"
                && data.TryGetProperty("mode", out var mode) && mode.GetString() == "payment"
                && data.TryGetProperty("metadata", out var meta))
            {
                if (meta.TryGetProperty("user_id", out var uid) && Guid.TryParse(uid.GetString(), out var parsedId))
                {
                    creditUserId = parsedId;
                }
                if (meta.TryGetProperty("credits", out var cred) && int.TryParse(cred.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedCredits))
                {
                    credits = parsedCredits;
                }
            }

            return new BillingEvent(root.GetProperty("id").GetString()!, eventType, customer, creditUserId, credits);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Signed by Stripe but not the shape we expect: nothing to act on.
            LogUnreadableEvent(logger, exception.Message);
            return null;
        }
    }

    /// <summary>Null for an object we cannot read: it is left out rather than guessed.</summary>
    /// <param name="tierOf">The offer a price id belongs to.</param>
    internal static SubscriptionState? StateOf(JsonElement subscription, Func<string?, Plan?> tierOf)
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
        var priceId = item is { } p && p.TryGetProperty("price", out var priceObject) && priceObject.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;
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
            subscription.TryGetProperty("cancel_at_period_end", out var cancel) && cancel.ValueKind == JsonValueKind.True,
            tierOf(priceId));
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
