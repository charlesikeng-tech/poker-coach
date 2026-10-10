using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Coaching;

/// <summary>
/// What every paid model call goes through (ADR-0008, ADR-0014): the model is configured, the plan opens the
/// feature, the user is under the feature's daily limit (and, on Free, its monthly allowance), the month is
/// under the global budget; afterwards, the call is billed to the ledger.
/// </summary>
internal sealed class CoachingGate(ICoachingStore store, ICoachingModel model, CoachingOptions options, PlanAccess plans, TimeProvider time)
{
    /// <param name="freeMonthlyAllowance">New texts a Free user gets per calendar month; 0: Pro only.</param>
    public async Task<CoachingFailure?> CheckAsync(Guid userId, string purpose, int dailyLimit, int freeMonthlyAllowance, CancellationToken cancellationToken)
    {
        if (!model.IsAvailable)
        {
            return CoachingFailure.Unavailable;
        }

        var now = time.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (await plans.GetPlanAsync(userId, cancellationToken) == Plan.Free)
        {
            if (freeMonthlyAllowance <= 0)
            {
                return CoachingFailure.PlanRequired;
            }

            // Counts billed calls, a rejected answer included: rare, and it keeps one ledger as the truth.
            if (await store.CountCallsSinceAsync(userId, purpose, monthStart, cancellationToken) >= freeMonthlyAllowance)
            {
                return CoachingFailure.PlanLimitReached;
            }
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
