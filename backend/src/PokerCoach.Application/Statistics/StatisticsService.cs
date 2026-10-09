using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Statistics;

/// <param name="To">Exclusive upper bound on the hand start time.</param>
/// <param name="MinStackBigBlinds">Inclusive lower bound on the hero's stack at the start of the hand.</param>
/// <param name="MaxStackBigBlinds">Exclusive upper bound.</param>
public sealed record StatisticsFilter(DateTimeOffset? From, DateTimeOffset? To, decimal? MinStackBigBlinds, decimal? MaxStackBigBlinds);

public interface IStatisticsReadStore
{
    /// <summary>Counts per position (null = unnamed) over hands of the user's confirmed accounts with current facts.</summary>
    Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken);

    /// <summary>Hands of the user's confirmed accounts whose facts are missing or outdated (still being computed).</summary>
    Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken);
}

/// <param name="Rate">Made / opportunities; null without opportunity. Never a guess.</param>
public sealed record StatRate(int Made, int Opportunities, decimal? Rate)
{
    public static StatRate Of(int made, int opportunities) =>
        new(made, opportunities, opportunities == 0 ? null : Math.Round((decimal)made / opportunities, 4));
}

public sealed record StatLine(
    int Hands,
    StatRate Vpip,
    StatRate Pfr,
    StatRate Rfi,
    StatRate Limp,
    StatRate Steal,
    StatRate ThreeBet,
    StatRate FoldToThreeBet,
    StatRate CbetFlop,
    StatRate WentToShowdown,
    StatRate WonAtShowdown,
    decimal? BigBlindsPer100)
{
    public static StatLine From(HeroStatCounts c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new StatLine(
            c.Hands,
            StatRate.Of(c.Vpip, c.PreflopDecisions),
            StatRate.Of(c.Pfr, c.PreflopDecisions),
            StatRate.Of(c.Rfi, c.RfiOpportunities),
            StatRate.Of(c.Limp, c.RfiOpportunities),
            StatRate.Of(c.Steal, c.StealOpportunities),
            StatRate.Of(c.ThreeBet, c.ThreeBetOpportunities),
            StatRate.Of(c.FoldToThreeBet, c.FoldToThreeBetOpportunities),
            StatRate.Of(c.CbetFlop, c.CbetFlopOpportunities),
            StatRate.Of(c.WentToShowdown, c.SawFlop),
            StatRate.Of(c.WonAtShowdown, c.WentToShowdown),
            c.Hands == 0 ? null : Math.Round(c.NetBigBlinds / c.Hands * 100m, 2));
    }
}

/// <param name="PendingHands">Hands whose facts are still being computed: figures are partial until 0.</param>
public sealed record StatisticsReport(StatLine Overall, IReadOnlyList<PositionStatLine> ByPosition, int PendingHands);

/// <param name="Position">Null when the position could not be named.</param>
public sealed record PositionStatLine(PokerPosition? Position, StatLine Line);

public sealed class StatisticsService(IStatisticsReadStore store)
{
    private static readonly PokerPosition[] Order =
    [
        PokerPosition.Utg, PokerPosition.Utg1, PokerPosition.Utg2, PokerPosition.Lojack, PokerPosition.Hijack,
        PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind, PokerPosition.BigBlind,
    ];

    public async Task<StatisticsReport> GetAsync(Guid userId, StatisticsFilter filter, CancellationToken cancellationToken)
    {
        var groups = await store.CountByPositionAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var pending = await store.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var overall = groups.Aggregate(HeroStatCounts.Zero, (total, g) => total.Add(g.Counts));
        var byPosition = groups
            .OrderBy(g => g.Position is { } p ? Array.IndexOf(Order, p) : int.MaxValue)
            .Select(g => new PositionStatLine(g.Position, StatLine.From(g.Counts)))
            .ToList();
        return new StatisticsReport(StatLine.From(overall), byPosition, pending);
    }
}
