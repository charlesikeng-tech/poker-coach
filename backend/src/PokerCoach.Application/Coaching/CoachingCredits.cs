namespace PokerCoach.Application.Coaching;

/// <summary>
/// AI credits (ADR-0016): each kind of coach text costs about what it costs us, so a plan's monthly credits
/// bound its AI spend whatever the mix. Counted from the usage ledger: a billed call counts, even when its
/// answer was then rejected.
/// </summary>
public static class CoachingCredits
{
    public static int CostOf(string purpose) => purpose switch
    {
        TournamentDebriefService.Kind => 5,
        WeekReviewService.Kind => 2,
        _ => 1,
    };

    private static readonly string[] Purposes = [LeakCoachService.Purpose, WeekReviewService.Kind, TournamentDebriefService.Kind];

    public static DateTimeOffset MonthStart(DateTimeOffset now) => new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<int> UsedSinceAsync(ICoachingStore ledger, Guid userId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var used = 0;
        foreach (var purpose in Purposes)
        {
            used += CostOf(purpose) * await ledger.CountCallsSinceAsync(userId, purpose, since, cancellationToken);
        }

        return used;
    }
}
