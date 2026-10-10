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
             "items":{"data":[{"current_period_end":1793000000,"price":{"recurring":{"interval":"year"}}}]}}
            """);

        var state = StripeBillingGateway.StateOf(json.RootElement);

        Assert.Equal(new SubscriptionState("sub_1", SubscriptionStatus.PastDue, BillingInterval.Year, DateTimeOffset.FromUnixTimeSeconds(1793000000), true), state);
    }

    [Fact]
    public void An_older_shape_or_an_unknown_status_is_handled_without_guessing()
    {
        using var older = JsonDocument.Parse("""{"id":"sub_2","status":"active","current_period_end":1793000000,"items":{"data":[]}}""");
        using var unknown = JsonDocument.Parse("""{"id":"sub_3","status":"something_new"}""");

        var state = StripeBillingGateway.StateOf(older.RootElement);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793000000), state?.CurrentPeriodEnd);
        Assert.Null(state?.Interval);
        Assert.False(state?.CancelAtPeriodEnd);
        Assert.Null(StripeBillingGateway.StateOf(unknown.RootElement));
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

    private static string Signed(string payload) =>
        $"t={Now.ToUnixTimeSeconds()},v1={StripeSignature.Sign(payload, Now.ToUnixTimeSeconds(), Secret)}";

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
