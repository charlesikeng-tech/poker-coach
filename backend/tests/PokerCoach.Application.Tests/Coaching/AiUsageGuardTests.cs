using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Tests.Subscriptions;

namespace PokerCoach.Application.Tests.Coaching;

public sealed class AiUsageGuardTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly ModelUsage Real = new("test-model", 6_000, 1_000, 0, 0);

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryAiUsageLedger ledger = new();
    private readonly ScriptedModel model = new();
    private readonly CoachingOptions options = new() { MonthlyBudgetUsd = 1m };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_successful_call_is_billed_at_its_real_cost_and_charges_its_credits()
    {
        var outcome = await Guard().RunAsync(UserId, AiTask.WeekReview, (_, _) => Answer(Real), Ct);

        Assert.Equal("ok", outcome.Answer!.Value);
        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(("Billed", 2, true), (entry.Status, entry.Credits, entry.Known));
        Assert.Equal(options.CostOf(Real), entry.CostUsd);
    }

    [Fact]
    public async Task A_failed_call_records_what_the_provider_billed_and_gives_the_credits_back()
    {
        var outcome = await Guard().RunAsync<string>(UserId, AiTask.TournamentDebrief, (_, _) => throw new CoachingModelException("rejected", Real), Ct);

        Assert.Equal(CoachingFailure.ModelFailed, outcome.Failure);
        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(("Failed", 0), (entry.Status, entry.Credits));
        Assert.Equal(options.CostOf(Real), entry.CostUsd);
        Assert.Equal(0, await Guard().CreditsUsedThisMonthAsync(UserId, Ct));
    }

    [Fact]
    public async Task When_the_provider_billing_is_unknown_the_reserved_ceiling_stays_counted()
    {
        await Guard().RunAsync<string>(UserId, AiTask.LeakExplanation, (_, _) => throw new CoachingModelException("timed out"), Ct);

        var entry = Assert.Single(ledger.Entries);
        Assert.False(entry.Known);
        Assert.Equal(options.CostOf(model.CeilingUsage), entry.CostUsd);
    }

    [Fact]
    public async Task An_unexpected_error_is_settled_at_the_ceiling_and_rethrown()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Guard().RunAsync<string>(UserId, AiTask.LeakExplanation, (_, _) => throw new InvalidOperationException("bug"), Ct));

        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(("Failed", false), (entry.Status, entry.Known));
    }

    [Fact]
    public async Task The_budget_must_have_room_for_the_worst_case_before_anything_runs()
    {
        // The ceiling is 1 000 output tokens = 0.01 USD; 0.995 already spent leaves too little room.
        ledger.Entries.Add(new InMemoryAiUsageLedger.Entry(Guid.NewGuid(), Guid.NewGuid(), AiTask.WeekReview, time.GetUtcNow()) { Status = "Billed", CostUsd = 0.995m });
        var prepared = false;

        var outcome = await Guard().RunAsync(UserId, AiTask.LeakExplanation, (_, _) => { prepared = true; return Answer(Real); }, Ct);

        Assert.Equal(CoachingFailure.BudgetExhausted, outcome.Failure);
        Assert.False(prepared);
    }

    [Fact]
    public async Task Calls_in_flight_count_against_credits_and_budget()
    {
        var release = new TaskCompletionSource();
        var plans = BillingFakes.Unbilled(time);
        var guard = new AiUsageGuard(ledger, model, new CoachingOptions { MonthlyBudgetUsd = 0.015m }, plans, time);

        var first = guard.RunAsync(UserId, AiTask.LeakExplanation, async (_, _) => { await release.Task; return new ModelAnswer<string>("ok", Real); }, Ct);
        var second = await guard.RunAsync(UserId, AiTask.LeakExplanation, (_, _) => Answer(Real), Ct);

        Assert.Equal(CoachingFailure.BudgetExhausted, second.Failure);
        release.SetResult();
        Assert.NotNull((await first).Answer);
    }

    [Fact]
    public async Task An_abandoned_reservation_frees_its_credits_but_keeps_its_cost()
    {
        var abandoned = new InMemoryAiUsageLedger.Entry(Guid.NewGuid(), UserId, AiTask.TournamentDebrief, time.GetUtcNow()) { Credits = 5, ReservedUsd = 0.5m };
        ledger.Entries.Add(abandoned);
        Assert.Equal(5, await Guard().CreditsUsedThisMonthAsync(UserId, Ct));

        time.Advance(AiUsageGuard.ReservationLifetime + TimeSpan.FromSeconds(1));

        Assert.Equal(0, await Guard().CreditsUsedThisMonthAsync(UserId, Ct));
        var outcome = await Guard().RunAsync(UserId, AiTask.LeakExplanation, (_, _) => Answer(Real), Ct);
        Assert.Null(outcome.Failure);

        // 0.5 still counted: a debrief ceiling no longer fits in the 1 USD budget alongside it and the explanation.
        model.CeilingUsage = new ModelUsage("test-model", 0, 50_000, 0, 0);
        Assert.Equal(CoachingFailure.BudgetExhausted, (await Guard().RunAsync(UserId, AiTask.TournamentDebrief, (_, _) => Answer(Real), Ct)).Failure);
    }

    [Fact]
    public async Task No_model_no_reservation()
    {
        model.IsAvailable = false;

        Assert.Equal(CoachingFailure.Unavailable, (await Guard().RunAsync(UserId, AiTask.LeakExplanation, (_, _) => Answer(Real), Ct)).Failure);
        Assert.Empty(ledger.Entries);
    }

    private AiUsageGuard Guard() => new(ledger, model, options, BillingFakes.Unbilled(time), time);

    private static Task<ModelAnswer<string>> Answer(ModelUsage usage) => Task.FromResult(new ModelAnswer<string>("ok", usage));
}
