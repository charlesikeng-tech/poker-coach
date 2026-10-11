using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Coaching;

/// <summary>
/// What every paid model call goes through (ADR-0008, ADR-0016): the model is configured, the plan opens the
/// feature, the month's AI credits cover the call, the user is under the feature's daily limit, the month is
/// under the global budget; afterwards, the call is billed to the ledger.
/// </summary>
internal sealed class CoachingGate(ICoachingStore store, ICoachingModel model, CoachingOptions options, PlanAccess plans, TimeProvider time)
{
    public const int ProReviewsPerMonth = 1;

    /// <param name="feature">The feature the call belongs to, when the plans gate it; null: every plan (credits apply).</param>
    public async Task<CoachingFailure?> CheckAsync(Guid userId, string purpose, int dailyLimit, Feature? feature, CancellationToken cancellationToken)
    {
        if (!model.IsAvailable)
        {
            return CoachingFailure.Unavailable;
        }

        var access = await plans.GetAccessAsync(userId, cancellationToken);
        if (feature is { } gated && !access.Plan.Allows(gated))
        {
            return CoachingFailure.PlanRequired;
        }

        var now = time.GetUtcNow();
        var monthStart = CoachingCredits.MonthStart(now);

        // Pro gets the coach's review once a month, Elite every week (ADR-0016).
        if (feature == Feature.WeekReview && access.Plan == Plan.Pro
            && await store.CountCallsSinceAsync(userId, purpose, monthStart, cancellationToken) >= ProReviewsPerMonth)
        {
            return CoachingFailure.PlanLimitReached;
        }

        // Administrators are bounded by the daily limits and the global budget only.
        if (access.Source != PlanSource.Admin
            && await CoachingCredits.UsedSinceAsync(store, userId, monthStart, cancellationToken) + CoachingCredits.CostOf(purpose)
                > plans.Limits.For(access.Plan).MonthlyCredits)
        {
            return CoachingFailure.PlanLimitReached;
        }

        var today = new DateTimeOffset(now.Date, TimeSpan.Zero);
        if (await store.CountCallsSinceAsync(userId, purpose, today, cancellationToken) >= dailyLimit)
        {
            return CoachingFailure.DailyLimitReached;
        }

        // Soft by at most the calls in flight: acceptable for a beta cap (ADR-0008).
        return await store.SpendSinceAsync(monthStart, cancellationToken) >= options.MonthlyBudgetUsd
            ? CoachingFailure.BudgetExhausted
            : null;
    }

    /// <summary>Recorded even if the answer is then rejected: it was billed.</summary>
    public Task RecordAsync(Guid userId, string purpose, ModelUsage usage, CancellationToken cancellationToken) =>
        store.RecordUsageAsync(userId, purpose, usage, options.CostOf(usage), cancellationToken);
}
