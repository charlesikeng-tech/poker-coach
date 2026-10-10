using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Application.Tests.Coaching;
using PokerCoach.Application.Tests.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Tests.Subscriptions;

public sealed class SubscriptionServiceTests
{
    private static readonly Uri AppUrl = new("https://app.nutsiq.test/");

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemorySubscriptionStore store = new();
    private readonly FakeBillingGateway stripe = new();
    private readonly InMemoryUserAccountStore users = new();
    private readonly User jo;

    public SubscriptionServiceTests()
    {
        jo = User.Register("Jo", "jo@example.com", "fr", time.GetUtcNow());
        users.AddUser(jo);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checkout_opens_one_customer_and_sends_to_stripe_with_our_urls()
    {
        var first = await Service().StartCheckoutAsync(jo.Id, BillingInterval.Year, "fr", AppUrl, Ct);
        var second = await Service().StartCheckoutAsync(jo.Id, BillingInterval.Month, "fr", AppUrl, Ct);

        Assert.Equal(new Uri("https://checkout.stripe.test/cus_1/Year"), first.Url);
        Assert.Equal(new Uri("https://checkout.stripe.test/cus_1/Month"), second.Url);
        Assert.Equal("cus_1", Assert.Single(store.Rows).CustomerId);
        Assert.Equal(new Uri("https://app.nutsiq.test/subscription?checkout=success"), stripe.Checkouts[0].SuccessUrl);
        Assert.Equal(Plan.Free, await Plans().GetPlanAsync(jo.Id, Ct));
    }

    [Fact]
    public async Task A_webhook_rereads_stripe_so_duplicates_and_disorder_converge()
    {
        await Service().StartCheckoutAsync(jo.Id, BillingInterval.Month, "fr", AppUrl, Ct);
        var renewal = time.GetUtcNow().AddDays(30);
        stripe.Subscriptions["cus_1"].Add(new SubscriptionState("sub_1", SubscriptionStatus.Active, BillingInterval.Month, renewal, false));

        Assert.Equal(WebhookOutcome.Synced, await Service().HandleWebhookAsync("cus_1", "valid", Ct));
        Assert.Equal(WebhookOutcome.Synced, await Service().HandleWebhookAsync("cus_1", "valid", Ct));

        var row = Assert.Single(store.Rows);
        Assert.Equal(Plan.Pro, row.Plan);
        Assert.Equal(renewal, row.CurrentPeriodEnd);

        // Canceled in Stripe: whatever event arrives next, the row follows Stripe.
        stripe.Subscriptions["cus_1"][0] = stripe.Subscriptions["cus_1"][0] with { Status = SubscriptionStatus.Canceled };
        await Service().HandleWebhookAsync("cus_1", "valid", Ct);
        Assert.Equal(Plan.Free, row.Plan);
    }

    [Fact]
    public async Task Forged_unknown_or_customerless_webhooks_change_nothing()
    {
        await Service().StartCheckoutAsync(jo.Id, BillingInterval.Month, "fr", AppUrl, Ct);
        stripe.Subscriptions["cus_1"].Add(new SubscriptionState("sub_1", SubscriptionStatus.Active, BillingInterval.Month, null, false));

        Assert.Equal(WebhookOutcome.Rejected, await Service().HandleWebhookAsync("cus_1", "forged", Ct));
        Assert.Equal(WebhookOutcome.Ignored, await Service().HandleWebhookAsync("cus_999", "valid", Ct));
        Assert.Equal(WebhookOutcome.Ignored, await Service().HandleWebhookAsync(string.Empty, "valid", Ct));
        Assert.Equal(Plan.Free, Assert.Single(store.Rows).Plan);
    }

    [Fact]
    public async Task A_pro_account_is_sent_to_the_portal_not_to_a_second_subscription()
    {
        await SubscribeAsync(SubscriptionStatus.Active);

        Assert.Equal(BillingFailure.AlreadySubscribed, (await Service().StartCheckoutAsync(jo.Id, BillingInterval.Year, "fr", AppUrl, Ct)).Failure);
        Assert.Equal(new Uri("https://billing.stripe.test/cus_1"), (await Service().OpenPortalAsync(jo.Id, AppUrl, Ct)).Url);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active, Plan.Pro)]
    [InlineData(SubscriptionStatus.Trialing, Plan.Pro)]
    [InlineData(SubscriptionStatus.PastDue, Plan.Pro)]
    [InlineData(SubscriptionStatus.Unpaid, Plan.Free)]
    [InlineData(SubscriptionStatus.Canceled, Plan.Free)]
    [InlineData(SubscriptionStatus.Incomplete, Plan.Free)]
    public async Task The_plan_follows_stripe_status_with_a_grace_while_stripe_retries_the_card(SubscriptionStatus status, Plan plan)
    {
        await SubscribeAsync(status);

        Assert.Equal(plan, await Plans().GetPlanAsync(jo.Id, Ct));
    }

    [Fact]
    public async Task Of_several_subscriptions_the_alive_one_decides()
    {
        var states = new[]
        {
            new SubscriptionState("sub_old", SubscriptionStatus.Canceled, BillingInterval.Month, time.GetUtcNow().AddDays(60), false),
            new SubscriptionState("sub_new", SubscriptionStatus.Active, BillingInterval.Year, time.GetUtcNow().AddDays(20), false),
        };

        Assert.Equal("sub_new", Subscription.Current(states)?.SubscriptionId);
        Assert.Null(Subscription.Current([]));
    }

    [Fact]
    public async Task Without_billing_everyone_has_pro_and_billing_actions_say_unavailable()
    {
        var off = new FakeBillingGateway(configured: false);
        var service = Service(off);

        Assert.Equal(Plan.Pro, (await service.GetAsync(jo.Id, Ct)).Plan);
        Assert.False((await service.GetAsync(jo.Id, Ct)).BillingEnabled);
        Assert.Equal(BillingFailure.Unavailable, (await service.StartCheckoutAsync(jo.Id, BillingInterval.Month, "fr", AppUrl, Ct)).Failure);
        Assert.Null(await Plans(off).HistoryStartAsync(jo.Id, Ct));
    }

    [Fact]
    public async Task Free_history_starts_thirty_days_ago_and_never_widens_a_narrower_request()
    {
        var plans = Plans();
        var start = time.GetUtcNow().AddDays(-30);

        Assert.Equal(start, await plans.HistoryStartAsync(jo.Id, Ct));
        Assert.Equal(start, await plans.ClampFromAsync(jo.Id, null, Ct));
        Assert.Equal(start, await plans.ClampFromAsync(jo.Id, time.GetUtcNow().AddYears(-1), Ct));
        Assert.Equal(time.GetUtcNow().AddDays(-7), await plans.ClampFromAsync(jo.Id, time.GetUtcNow().AddDays(-7), Ct));

        await SubscribeAsync(SubscriptionStatus.Active);
        Assert.Null(await plans.ClampFromAsync(jo.Id, null, Ct));
    }

    [Fact]
    public async Task A_stale_record_is_reread_when_shown_and_a_stripe_outage_shows_the_last_state()
    {
        await SubscribeAsync(SubscriptionStatus.Active);
        stripe.Subscriptions["cus_1"].Clear();

        time.Advance(TimeSpan.FromHours(2));
        Assert.Equal(Plan.Pro, (await Service().GetAsync(jo.Id, Ct)).Plan);

        time.Advance(SubscriptionService.MaxSyncAge);
        stripe.Failing = true;
        Assert.Equal(Plan.Pro, (await Service().GetAsync(jo.Id, Ct)).Plan);

        stripe.Failing = false;
        Assert.Equal(Plan.Free, (await Service().GetAsync(jo.Id, Ct)).Plan);
    }

    [Fact]
    public async Task Closing_an_account_deletes_the_customer_and_refuses_when_stripe_fails()
    {
        await SubscribeAsync(SubscriptionStatus.Active);

        stripe.Failing = true;
        Assert.False(await Service().CloseAsync(jo.Id, Ct));
        Assert.Empty(stripe.Deleted);

        stripe.Failing = false;
        Assert.True(await Service().CloseAsync(jo.Id, Ct));
        Assert.Equal("cus_1", Assert.Single(stripe.Deleted));
        Assert.True(await Service().CloseAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task Free_accounts_get_three_explanations_a_month_and_no_debrief()
    {
        var ledger = new MonthLedger(time);
        var gate = new CoachingGate(ledger, new ScriptedModel(), new CoachingOptions(), Plans(), time);

        Assert.Equal(CoachingFailure.PlanRequired, await gate.CheckAsync(jo.Id, "tournament-debrief", 10, 0, Ct));
        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 20, 3, Ct));
            ledger.Calls.Add(time.GetUtcNow());
            time.Advance(TimeSpan.FromDays(1));
        }

        Assert.Equal(CoachingFailure.PlanLimitReached, await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 20, 3, Ct));

        time.SetUtcNow(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Null(await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 20, 3, Ct));

        await SubscribeAsync(SubscriptionStatus.Active);
        Assert.Null(await gate.CheckAsync(jo.Id, "tournament-debrief", 10, 0, Ct));
    }

    private async Task SubscribeAsync(SubscriptionStatus status)
    {
        await Service().StartCheckoutAsync(jo.Id, BillingInterval.Month, "fr", AppUrl, Ct);
        stripe.Subscriptions["cus_1"].Clear();
        stripe.Subscriptions["cus_1"].Add(new SubscriptionState("sub_1", status, BillingInterval.Month, time.GetUtcNow().AddDays(30), false));
        await Service().HandleWebhookAsync("cus_1", "valid", Ct);
    }

    private PlanAccess Plans(FakeBillingGateway? gateway = null) => new(store, gateway ?? stripe, new FreePlanOptions(), time);

    private SubscriptionService Service(FakeBillingGateway? gateway = null) =>
        new(store, gateway ?? stripe, Plans(gateway), new UserProfileService(users), new LedgerStore(), time);

    /// <summary>Billed calls with their time: counts depend on the window asked.</summary>
    private sealed class MonthLedger(TimeProvider time) : ICoachingStore
    {
        public List<DateTimeOffset> Calls { get; } = [];

        public Task<int> CountCallsSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(Calls.Count(c => c >= since && c <= time.GetUtcNow()));

        public Task<decimal> SpendSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0m);

        public Task RecordUsageAsync(Guid? userId, string purpose, ModelUsage usage, decimal costUsd, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<StoredExplanation?> FindExplanationAsync(Guid userId, string fingerprint, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveExplanationAsync(Guid userId, string fingerprint, string language, StoredExplanation explanation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExampleHand>> FindExampleHandsAsync(Guid userId, LeakSituation situation, int count, int factsVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
