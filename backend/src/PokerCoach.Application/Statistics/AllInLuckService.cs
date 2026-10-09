using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Statistics;

/// <summary>One preflop all-in with every hand shown, as stored in the hero's facts.</summary>
/// <param name="HeroCards">"AhKd"; null if unknown (never for a counted all-in, kept nullable as stored).</param>
public sealed record AllInHand(
    Guid HandId,
    Guid TournamentId,
    DateTimeOffset StartedAt,
    long BigBlind,
    string? HeroCards,
    decimal Equity,
    decimal ExpectedNetChips,
    long NetChips);

public interface IAllInReadStore
{
    /// <summary>The user's counted all-ins (current facts only), oldest first.</summary>
    Task<IReadOnlyList<AllInHand>> ListAllInsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken);
}

/// <param name="Index">Position of the all-in, from 1, oldest first.</param>
public sealed record LuckPoint(int Index, DateTimeOffset StartedAt, decimal ActualBigBlinds, decimal ExpectedBigBlinds);

/// <param name="Luck">Actual minus expected, in big blinds: positive when the board was kind.</param>
public sealed record LuckSwing(Guid HandId, Guid TournamentId, DateTimeOffset StartedAt, string? HeroCards, decimal Equity, decimal NetBigBlinds, decimal Luck);

/// <param name="Won">All-ins the hero came out of with more chips than he put in.</param>
/// <param name="ExpectedWins">Sum of the hero's main-pot equities: how many he "should" have won.</param>
/// <param name="ActualBigBlinds">What the all-ins actually returned, in big blinds.</param>
/// <param name="ExpectedBigBlinds">What they should have returned given the cards.</param>
/// <param name="Luck">Actual minus expected.</param>
/// <param name="Curve">Cumulative actual and expected, one point per all-in.</param>
/// <param name="Swings">Biggest gaps between actual and expected, worst beats and luckiest outdraws mixed.</param>
public sealed record AllInLuckReport(
    int AllIns,
    int Won,
    decimal ExpectedWins,
    decimal? AverageEquity,
    decimal ActualBigBlinds,
    decimal ExpectedBigBlinds,
    decimal Luck,
    IReadOnlyList<LuckPoint> Curve,
    IReadOnlyList<LuckSwing> Swings,
    int PendingHands);

/// <summary>
/// Luck at the all-ins: actual against expected results of preflop all-ins with every hand shown
/// (<see cref="AllInExpectation"/>). In big blinds, so levels and tournaments add up.
/// </summary>
public sealed class AllInLuckService(IAllInReadStore store, IStatisticsReadStore statistics)
{
    internal const int SwingCount = 8;

    public async Task<AllInLuckReport> GetAsync(Guid userId, StatisticsFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var hands = await store.ListAllInsAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var pending = await statistics.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var rows = hands
            .Where(h => h.BigBlind > 0)
            .OrderBy(h => h.StartedAt)
            .ThenBy(h => h.HandId)
            .Select(h => (Hand: h, Actual: (decimal)h.NetChips / h.BigBlind, Expected: h.ExpectedNetChips / h.BigBlind))
            .ToList();

        var curve = new List<LuckPoint>(rows.Count);
        decimal actual = 0, expected = 0;
        foreach (var row in rows)
        {
            actual += row.Actual;
            expected += row.Expected;
            curve.Add(new LuckPoint(curve.Count + 1, row.Hand.StartedAt, Math.Round(actual, 2), Math.Round(expected, 2)));
        }

        var swings = rows
            .OrderByDescending(r => Math.Abs(r.Actual - r.Expected))
            .Take(SwingCount)
            .Select(r => new LuckSwing(
                r.Hand.HandId,
                r.Hand.TournamentId,
                r.Hand.StartedAt,
                r.Hand.HeroCards,
                r.Hand.Equity,
                Math.Round(r.Actual, 2),
                Math.Round(r.Actual - r.Expected, 2)))
            .ToList();

        return new AllInLuckReport(
            rows.Count,
            rows.Count(r => r.Hand.NetChips > 0),
            Math.Round(rows.Sum(r => r.Hand.Equity), 2),
            rows.Count == 0 ? null : Math.Round(rows.Average(r => r.Hand.Equity), 4),
            Math.Round(actual, 2),
            Math.Round(expected, 2),
            Math.Round(actual - expected, 2),
            curve,
            swings,
            pending);
    }
}
