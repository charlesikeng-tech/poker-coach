using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PokerCoach.Domain.Subscriptions;
using PokerCoach.Infrastructure.Billing;

namespace PokerCoach.Api.Tests;

/// <summary>The Stripe adapter's pure parts: no database, no network.</summary>
public sealed class StripeAdapterTests
{
    private const string Secret = "whsec_test";

    private static readonly StripePlanPrices Prices = new()
    {
        Pro = new StripePrices { Monthly = "price_pro_m", Yearly = "price_pro_y" },
        Elite = new StripePrices { Monthly = "price_elite_m", Yearly = "price_elite_y" },
    };
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_signature_is_valid_only_with_the_secret_the_exact_payload_and_a_recent_time()
    {
        const string payload = """{"id":"evt_1"}""";
        var t = Now.ToUnixTimeSeconds();
        var header = $"t={t},v1={StripeSignature.Sign(payload, t, Secret)}";
        var tolerance = TimeSpan.FromMinutes(5);

        Assert.True(StripeSignature.IsValid(payload, header, Secret, Now, tolerance));
        Assert.True(StripeSignature.IsValid(payload, $"t={t},v1=deadbeef,v1={StripeSignature.Sign(payload, t, Secret)}", Secret, Now, tolerance));
        Assert.False(StripeSignature.IsValid(payload + " ", header, Secret, Now, tolerance));
        Assert.False(StripeSignature.IsValid(payload, header, "whsec_other", Now, tolerance));
        Assert.False(StripeSignature.IsValid(payload, header, Secret, Now.AddMinutes(6), tolerance));
        Assert.False(StripeSignature.IsValid(payload, $"v1={StripeSignature.Sign(payload, t, Secret)}", Secret, Now, tolerance));
        Assert.False(StripeSignature.IsValid(payload, null, Secret, Now, tolerance));
    }

    [Fact]
    public void A_subscription_reads_its_period_from_the_item_in_recent_api_versions()
    {
        using var json = JsonDocument.Parse("""
            {"id":"sub_1","status":"past_due","cancel_at_period_end":true,
             "items":{"data":[{"current_period_end":1793000000,"price":{"id":"price_elite_y","recurring":{"interval":"year"}}}]}}
            """);

        var state = StripeBillingGateway.StateOf(json.RootElement, Prices.TierOf);

        Assert.Equal(new SubscriptionState("sub_1", SubscriptionStatus.PastDue, BillingInterval.Year, DateTimeOffset.FromUnixTimeSeconds(1793000000), true, Plan.Elite), state);
    }

    [Fact]
    public void An_older_shape_or_an_unknown_status_is_handled_without_guessing()
    {
        using var older = JsonDocument.Parse("""{"id":"sub_2","status":"active","current_period_end":1793000000,"items":{"data":[]}}""");
        using var unknown = JsonDocument.Parse("""{"id":"sub_3","status":"something_new"}""");

        var state = StripeBillingGateway.StateOf(older.RootElement, Prices.TierOf);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793000000), state?.CurrentPeriodEnd);
        Assert.Null(state?.Interval);
        Assert.Null(state?.Tier);
        Assert.False(state?.CancelAtPeriodEnd);
        Assert.Null(StripeBillingGateway.StateOf(unknown.RootElement, Prices.TierOf));
    }

    [Fact]
    public void A_signed_event_names_its_customer_whatever_the_object()
    {
        using var http = new HttpClient();
        var gateway = new StripeBillingGateway(
            http,
            Options.Create(new StripeOptions { SecretKey = "sk_test", WebhookSecret = Secret }),
            new FixedTime(Now),
            NullLogger<StripeBillingGateway>.Instance);
        const string updated = """{"id":"evt_1","type":"customer.subscription.updated","data":{"object":{"object":"subscription","customer":"cus_9"}}}""";
        const string deleted = """{"id":"evt_2","type":"customer.deleted","data":{"object":{"object":"customer","id":"cus_9"}}}""";
        const string other = """{"id":"evt_3","type":"product.created","data":{"object":{"object":"product","id":"prod_1"}}}""";

        Assert.Equal(new("evt_1", "customer.subscription.updated", "cus_9"), gateway.ReadWebhook(updated, Signed(updated)));
        Assert.Equal("cus_9", gateway.ReadWebhook(deleted, Signed(deleted))?.CustomerId);
        Assert.Null(gateway.ReadWebhook(other, Signed(other))?.CustomerId);
        Assert.Null(gateway.ReadWebhook(updated, "t=1,v1=00"));
    }

    [Fact]
    public async Task A_change_of_offer_opens_the_portal_on_the_confirmation_of_the_new_price()
    {
        var handler = new Recorder(
            """{"id":"sub_1","items":{"data":[{"id":"si_1"}]}}""",
            """{"url":"https://billing.stripe.com/p/session/x"}""");
        using var http = new HttpClient(handler);
        var gateway = new StripeBillingGateway(
            http,
            Options.Create(new StripeOptions { SecretKey = "sk_test", Prices = Prices }),
            new FixedTime(Now),
            NullLogger<StripeBillingGateway>.Instance);

        var url = await gateway.CreatePlanChangeAsync("cus_1", "sub_1", Plan.Elite, BillingInterval.Year, new Uri("https://app.test/subscription"), TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://billing.stripe.com/p/session/x"), url);
        Assert.Equal("/v1/subscriptions/sub_1", handler.Paths[0]);
        var form = handler.Bodies[1];
        Assert.Contains("flow_data%5Btype%5D=subscription_update_confirm", form, StringComparison.Ordinal);
        Assert.Contains("flow_data%5Bsubscription_update_confirm%5D%5Bitems%5D%5B0%5D%5Bid%5D=si_1", form, StringComparison.Ordinal);
        Assert.Contains("flow_data%5Bsubscription_update_confirm%5D%5Bitems%5D%5B0%5D%5Bprice%5D=price_elite_y", form, StringComparison.Ordinal);
    }

    private sealed class Recorder(params string[] answers) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(answers[Paths.Count - 1]) };
        }
    }

    private static string Signed(string payload) =>
        $"t={Now.ToUnixTimeSeconds()},v1={StripeSignature.Sign(payload, Now.ToUnixTimeSeconds(), Secret)}";

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
