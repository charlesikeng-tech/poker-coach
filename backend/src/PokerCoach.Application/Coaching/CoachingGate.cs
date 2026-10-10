namespace PokerCoach.Application.Coaching;

/// <summary>
/// What every paid model call goes through (ADR-0008): the model is configured, the user is under the
/// feature's daily limit, the month is under the global budget; afterwards, the call is billed to the ledger.
/// </summary>
internal sealed class CoachingGate(ICoachingStore store, ICoachingModel model, CoachingOptions options, TimeProvider time)
{
    public async Task<CoachingFailure?> CheckAsync(Guid userId, string purpose, int dailyLimit, CancellationToken cancellationToken)
    {
        if (!model.IsAvailable)
        {
            return CoachingFailure.Unavailable;
        }

        var now = time.GetUtcNow();
        var today = new DateTimeOffset(now.Date, TimeSpan.Zero);
        if (await store.CountCallsSinceAsync(userId, purpose, today, cancellationToken) >= dailyLimit)
        {
            return CoachingFailure.DailyLimitReached;
        }

        // Soft by at most the calls in flight: acceptable for a beta cap (ADR-0008).
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return await store.SpendSinceAsync(monthStart, cancellationToken) >= options.MonthlyBudgetUsd
            ? CoachingFailure.BudgetExhausted
            : null;
    }

    /// <summary>Recorded even if the answer is then rejected: it was billed.</summary>
    public Task RecordAsync(Guid userId, string purpose, ModelUsage usage, CancellationToken cancellationToken) =>
        store.RecordUsageAsync(userId, purpose, usage, options.CostOf(usage), cancellationToken);
}
