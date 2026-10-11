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
        var first = await Service().StartCheckoutAsync(jo.Id, Plan.Pro, BillingInterval.Year, "fr", AppUrl, Ct);
        var second = await Service().StartCheckoutAsync(jo.Id, Plan.Pro, BillingInterval.Month, "fr", AppUrl, Ct);

        Assert.Equal(new Uri("https://checkout.stripe.test/cus_1/Year"), first.Url);
        Assert.Equal(new Uri("https://checkout.stripe.test/cus_1/Month"), second.Url);
        Assert.Equal("cus_1", Assert.Single(store.Rows).CustomerId);
        Assert.Equal(new Uri("https://app.nutsiq.test/subscription?checkout=success"), stripe.Checkouts[0].SuccessUrl);
        Assert.Equal(Plan.Starter, await Plans().GetPlanAsync(jo.Id, Ct));
    }

    [Fact]
    public async Task A_webhook_rereads_stripe_so_duplicates_and_disorder_converge()
    {
        await Service().StartCheckoutAsync(jo.Id, Plan.Pro, BillingInterval.Month, "fr", AppUrl, Ct);
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
        Assert.Equal(Plan.Starter, row.Plan);
    }

    [Fact]
    public async Task Forged_unknown_or_customerless_webhooks_change_nothing()
    {
        await Service().StartCheckoutAsync(jo.Id, Plan.Pro, BillingInterval.Month, "fr", AppUrl, Ct);
        stripe.Subscriptions["cus_1"].Add(new SubscriptionState("sub_1", SubscriptionStatus.Active, BillingInterval.Month, null, false));

        Assert.Equal(WebhookOutcome.Rejected, await Service().HandleWebhookAsync("cus_1", "forged", Ct));
        Assert.Equal(WebhookOutcome.Ignored, await Service().HandleWebhookAsync("cus_999", "valid", Ct));
        Assert.Equal(WebhookOutcome.Ignored, await Service().HandleWebhookAsync(string.Empty, "valid", Ct));
        Assert.Equal(Plan.Starter, Assert.Single(store.Rows).Plan);
    }

    [Fact]
    public async Task A_pro_account_is_sent_to_the_portal_not_to_a_second_subscription()
    {
        await SubscribeAsync(SubscriptionStatus.Active);

        Assert.Equal(BillingFailure.AlreadySubscribed, (await Service().StartCheckoutAsync(jo.Id, Plan.Pro, BillingInterval.Year, "fr", AppUrl, Ct)).Failure);
        Assert.Equal(new Uri("https://billing.stripe.test/cus_1"), (await Service().OpenPortalAsync(jo.Id, AppUrl, Ct)).Url);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active, Plan.Pro)]
    [InlineData(SubscriptionStatus.Trialing, Plan.Pro)]
    [InlineData(SubscriptionStatus.PastDue, Plan.Pro)]
    [InlineData(SubscriptionStatus.Unpaid, Plan.Starter)]
    [InlineData(SubscriptionStatus.Canceled, Plan.Starter)]
    [InlineData(SubscriptionStatus.Incomplete, Plan.Starter)]
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
    public async Task Without_billing_everyone_has_elite_and_billing_actions_say_unavailable()
    {
        var off = new FakeBillingGateway(configured: false);
        var service = Service(off);

        Assert.Equal(Plan.Elite, (await service.GetAsync(jo.Id, Ct)).Plan);
        Assert.False((await service.GetAsync(jo.Id, Ct)).BillingEnabled);
        Assert.Equal(BillingFailure.Unavailable, (await service.StartCheckoutAsync(jo.Id, Plan.Pro, BillingInterval.Month, "fr", AppUrl, Ct)).Failure);
        Assert.Null(await Plans(off).HistoryStartAsync(jo.Id, Ct));
    }

    [Fact]
    public async Task Starter_history_starts_thirty_days_ago_and_never_widens_a_narrower_request()
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
        Assert.Equal(Plan.Starter, (await Service().GetAsync(jo.Id, Ct)).Plan);
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
    public async Task Starter_gets_three_credits_a_month_and_neither_review_nor_debrief()
    {
        var ledger = new MonthLedger(time);
        var gate = Gate(ledger);

        Assert.Equal(CoachingFailure.PlanRequired, await gate.CheckAsync(jo.Id, TournamentDebriefService.Kind, 10, Feature.TournamentDebrief, Ct));
        Assert.Equal(CoachingFailure.PlanRequired, await gate.CheckAsync(jo.Id, WeekReviewService.Kind, 10, Feature.WeekReview, Ct));
        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 20, null, Ct));
            ledger.Add(LeakCoachService.Purpose);
            time.Advance(TimeSpan.FromDays(1));
        }

        Assert.Equal(CoachingFailure.PlanLimitReached, await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 20, null, Ct));

        time.SetUtcNow(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Null(await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 20, null, Ct));
    }

    [Fact]
    public async Task Pro_has_one_review_a_month_and_no_debrief_and_credits_weigh_what_texts_cost()
    {
        await SubscribeAsync(SubscriptionStatus.Active, Plan.Pro);
        var ledger = new MonthLedger(time);
        var gate = Gate(ledger);

        Assert.Equal(CoachingFailure.PlanRequired, await gate.CheckAsync(jo.Id, TournamentDebriefService.Kind, 10, Feature.TournamentDebrief, Ct));
        Assert.Null(await gate.CheckAsync(jo.Id, WeekReviewService.Kind, 10, Feature.WeekReview, Ct));
        ledger.Add(WeekReviewService.Kind);
        Assert.Equal(CoachingFailure.PlanLimitReached, await gate.CheckAsync(jo.Id, WeekReviewService.Kind, 10, Feature.WeekReview, Ct));

        // 2 credits used by the review: 98 explanations left of 100.
        ledger.Add(LeakCoachService.Purpose, 97);
        Assert.Null(await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 200, null, Ct));
        ledger.Add(LeakCoachService.Purpose);
        Assert.Equal(CoachingFailure.PlanLimitReached, await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 200, null, Ct));
        Assert.Equal(100, (await Service(ledger: ledger).GetAsync(jo.Id, Ct)).CreditsUsedThisMonth);
    }

    [Fact]
    public async Task Elite_has_debriefs_at_five_credits_and_weekly_reviews()
    {
        await SubscribeAsync(SubscriptionStatus.Active, Plan.Elite);
        var ledger = new MonthLedger(time);
        var gate = Gate(ledger);

        Assert.Equal(Plan.Elite, await Plans().GetPlanAsync(jo.Id, Ct));
        ledger.Add(WeekReviewService.Kind, 2);
        Assert.Null(await gate.CheckAsync(jo.Id, WeekReviewService.Kind, 10, Feature.WeekReview, Ct));

        // 4 credits used: 79 debriefs (395) fit in 400, the 80th does not.
        ledger.Add(TournamentDebriefService.Kind, 79);
        Assert.Equal(CoachingFailure.PlanLimitReached, await gate.CheckAsync(jo.Id, TournamentDebriefService.Kind, 1000, Feature.TournamentDebrief, Ct));
        Assert.Null(await gate.CheckAsync(jo.Id, LeakCoachService.Purpose, 1000, null, Ct));
    }

    [Fact]
    public async Task A_price_we_do_not_know_counts_as_pro_never_more()
    {
        await SubscribeAsync(SubscriptionStatus.Active, tier: null);

        Assert.Equal(Plan.Pro, await Plans().GetPlanAsync(jo.Id, Ct));
    }

    [Fact]
    public async Task The_higher_of_paid_and_given_applies()
    {
        await SubscribeAsync(SubscriptionStatus.Active, Plan.Pro);
        await Admin().GrantAsync(Guid.NewGuid(), "jo@example.com", Plan.Elite, "Partenaire", null, Ct);

        Assert.Equal(new PlanGrant(Plan.Elite, PlanSource.Grant), await Plans().GetAccessAsync(jo.Id, Ct));

        await Admin().GrantAsync(Guid.NewGuid(), "jo@example.com", Plan.Pro, "Partenaire", null, Ct);
        Assert.Equal(new PlanGrant(Plan.Pro, PlanSource.Subscription), await Plans().GetAccessAsync(jo.Id, Ct));
    }

    [Fact]
    public async Task Checkout_buys_the_chosen_paid_offer()
    {
        await Service().StartCheckoutAsync(jo.Id, Plan.Elite, BillingInterval.Year, "fr", AppUrl, Ct);

        Assert.Equal(Plan.Elite, Assert.Single(stripe.Checkouts).Plan);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().StartCheckoutAsync(jo.Id, Plan.Starter, BillingInterval.Year, "fr", AppUrl, Ct));
    }

    [Theory]
    [InlineData(Plan.Starter, Feature.OpeningRanges, false)]
    [InlineData(Plan.Pro, Feature.OpeningRanges, true)]
    [InlineData(Plan.Pro, Feature.TrainingDrills, true)]
    [InlineData(Plan.Pro, Feature.WeekReview, true)]
    [InlineData(Plan.Pro, Feature.PushFoldRanges, false)]
    [InlineData(Plan.Pro, Feature.AdvancedLeaks, false)]
    [InlineData(Plan.Pro, Feature.TrainingOwnHands, false)]
    [InlineData(Plan.Pro, Feature.TournamentDebrief, false)]
    [InlineData(Plan.Elite, Feature.TournamentDebrief, true)]
    [InlineData(Plan.Elite, Feature.AdvancedLeaks, true)]
    public void The_offer_matrix_opens_features_from_their_lowest_plan(Plan plan, Feature feature, bool allowed)
    {
        Assert.Equal(allowed, plan.Allows(feature));
    }

    [Fact]
    public async Task Administrators_have_elite_without_paying_whatever_the_case_of_their_email()
    {
        admins.Emails.Add(" JO@example.com ");
        var other = User.Register("Max", "max@example.com", "fr", time.GetUtcNow());
        users.AddUser(other);

        Assert.Equal(new PlanGrant(Plan.Elite, PlanSource.Admin), await Plans().GetAccessAsync(jo.Id, Ct));
        Assert.True(await Plans().IsAdminAsync(jo.Id, Ct));
        Assert.Equal(new PlanGrant(Plan.Starter, PlanSource.Starter), await Plans().GetAccessAsync(other.Id, Ct));
        Assert.False(await Plans().IsAdminAsync(other.Id, Ct));
    }

    [Fact]
    public async Task An_offered_access_gives_pro_until_its_end_or_until_revoked()
    {
        var admin = Admin();
        var end = time.GetUtcNow().AddDays(30);

        Assert.Equal(GrantResult.Granted, await admin.GrantAsync(Guid.NewGuid(), "JO@example.com", Plan.Pro, "Streamer partenaire", end, Ct));
        Assert.Equal(new PlanGrant(Plan.Pro, PlanSource.Grant, end), await Plans().GetAccessAsync(jo.Id, Ct));
        Assert.Null(await Plans().HistoryStartAsync(jo.Id, Ct));

        time.Advance(TimeSpan.FromDays(31));
        Assert.Equal(Plan.Starter, await Plans().GetPlanAsync(jo.Id, Ct));

        await admin.GrantAsync(Guid.NewGuid(), "jo@example.com", Plan.Pro, "Prolongé", null, Ct);
        Assert.Equal(new PlanGrant(Plan.Pro, PlanSource.Grant), await Plans().GetAccessAsync(jo.Id, Ct));
        Assert.Equal("Prolongé", Assert.Single(await admin.ListGrantsAsync(Ct)).Reason);

        Assert.True(await admin.RevokeAsync(jo.Id, Ct));
        Assert.False(await admin.RevokeAsync(jo.Id, Ct));
        Assert.Equal(Plan.Starter, await Plans().GetPlanAsync(jo.Id, Ct));
    }

    [Fact]
    public async Task Grants_need_a_known_account_a_reason_and_a_future_end()
    {
        var admin = Admin();

        Assert.Equal(GrantResult.UserNotFound, await admin.GrantAsync(Guid.NewGuid(), "nobody@example.com", Plan.Pro, "x", null, Ct));
        Assert.Equal(GrantResult.UserNotFound, await admin.GrantAsync(Guid.NewGuid(), "not an email", Plan.Pro, "x", null, Ct));
        Assert.Equal(GrantResult.InvalidReason, await admin.GrantAsync(Guid.NewGuid(), "jo@example.com", Plan.Pro, " ", null, Ct));
        Assert.Equal(GrantResult.EndInPast, await admin.GrantAsync(Guid.NewGuid(), "jo@example.com", Plan.Pro, "x", time.GetUtcNow(), Ct));
        Assert.Equal(GrantResult.InvalidPlan, await admin.GrantAsync(Guid.NewGuid(), "jo@example.com", Plan.Starter, "x", null, Ct));
        Assert.Empty(store.Grants);
    }

    [Fact]
    public async Task A_paid_subscription_comes_before_a_grant_and_a_grantee_can_still_subscribe()
    {
        await Admin().GrantAsync(Guid.NewGuid(), "jo@example.com", Plan.Pro, "Ami", null, Ct);

        Assert.NotNull((await Service().StartCheckoutAsync(jo.Id, Plan.Pro, BillingInterval.Month, "fr", AppUrl, Ct)).Url);

        stripe.Subscriptions["cus_1"].Add(new SubscriptionState("sub_1", SubscriptionStatus.Active, BillingInterval.Month, null, false));
        await Service().HandleWebhookAsync("cus_1", "valid", Ct);
        Assert.Equal(PlanSource.Subscription, (await Service().GetAsync(jo.Id, Ct)).Source);
    }

    private AdminService Admin() => new(store, new InMemoryLocalAccountStore(users), time);

    private async Task SubscribeAsync(SubscriptionStatus status, Plan? tier = Plan.Pro)
    {
        if (!stripe.Subscriptions.ContainsKey("cus_1"))
        {
            await Service().StartCheckoutAsync(jo.Id, Plan.Pro, BillingInterval.Month, "fr", AppUrl, Ct);
        }

        stripe.Subscriptions["cus_1"].Clear();
        stripe.Subscriptions["cus_1"].Add(new SubscriptionState("sub_1", status, BillingInterval.Month, time.GetUtcNow().AddDays(30), false, tier));
        await Service().HandleWebhookAsync("cus_1", "valid", Ct);
    }

    private readonly AdminOptions admins = new();

    private PlanAccess Plans(FakeBillingGateway? gateway = null) => new(store, gateway ?? stripe, users, admins, new PlanOptions(), time);

    private SubscriptionService Service(FakeBillingGateway? gateway = null, ICoachingStore? ledger = null) =>
        new(store, gateway ?? stripe, Plans(gateway), new UserProfileService(users), ledger ?? new LedgerStore(), time);

    private CoachingGate Gate(ICoachingStore ledger) =>
        new(ledger, new ScriptedModel(), new CoachingOptions { DailyDebriefsPerUser = 1000 }, Plans(), time);

    /// <summary>Billed calls with their purpose and time: counts depend on the window asked.</summary>
    private sealed class MonthLedger(TimeProvider time) : ICoachingStore
    {
        private readonly List<(string Purpose, DateTimeOffset At)> calls = [];

        public void Add(string purpose, int times = 1)
        {
            for (var i = 0; i < times; i++)
            {
                calls.Add((purpose, time.GetUtcNow()));
            }
        }

        public Task<int> CountCallsSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(calls.Count(c => c.Purpose == purpose && c.At >= since && c.At <= time.GetUtcNow()));

        public Task<decimal> SpendSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0m);

        public Task RecordUsageAsync(Guid? userId, string purpose, ModelUsage usage, decimal costUsd, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<StoredExplanation?> FindExplanationAsync(Guid userId, string fingerprint, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveExplanationAsync(Guid userId, string fingerprint, string language, StoredExplanation explanation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExampleHand>> FindExampleHandsAsync(Guid userId, LeakSituation situation, int count, int factsVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
