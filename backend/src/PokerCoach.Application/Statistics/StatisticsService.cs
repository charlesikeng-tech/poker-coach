using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Statistics;

/// <param name="To">Exclusive upper bound on the hand start time.</param>
/// <param name="MinStackBigBlinds">Inclusive lower bound on the hero's stack at the start of the hand.</param>
/// <param name="MaxStackBigBlinds">Exclusive upper bound.</param>
/// <param name="CompleteHistoryOnly">Only hands of tournaments whose hand history is complete (no gap).</param>
/// <param name="Format">Only hands of that table format, positions then named within it (a 6-max UTG is not a
/// full-ring UTG); null: every format, raw position names.</param>
/// <param name="Phase">Only hands of that tournament phase (by blind level); null: all.</param>
public sealed record StatisticsFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    decimal? MinStackBigBlinds,
    decimal? MaxStackBigBlinds,
    bool CompleteHistoryOnly = false,
    TableFormat? Format = null,
    TournamentPhase? Phase = null);

/// <summary>Analysed hands per table format, within a filter (its own format ignored).</summary>
public sealed record FormatCounts(int SixMax, int FullRing)
{
    /// <summary>The format the player plays most; 6-max on a tie or without hands.</summary>
    public TableFormat Busiest => FullRing > SixMax ? TableFormat.FullRing : TableFormat.SixMax;
}

/// <summary>What the figures rest on: tournaments behind the filtered hands, and how many have a complete history.</summary>
public sealed record StatisticsSample(int Tournaments, int CompleteTournaments);

public interface IStatisticsReadStore
{
    /// <summary>Counts per position (null = unnamed) over hands of the user's confirmed accounts with current facts.</summary>
    Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken);

    Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken);

    /// <summary>Analysed hands per table format within the filter, ignoring its format.</summary>
    Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken);

    /// <summary>Counts per calendar month (UTC) within the filter, oldest first.</summary>
    Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken);

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
    decimal? BigBlindsPer100,
    StatRate FoldToCbetFlop,
    StatRate RaiseCbetFlop,
    StatRate CbetTurn,
    StatRate CheckRaiseFlop,
    StatRate WonWhenSawFlop,
    StatRate PostflopAggression)
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
            c.Hands == 0 ? null : Math.Round(c.NetBigBlinds / c.Hands * 100m, 2),
            StatRate.Of(c.FoldToCbetFlop, c.FoldToCbetFlopOpportunities),
            StatRate.Of(c.RaiseCbetFlop, c.FoldToCbetFlopOpportunities),
            StatRate.Of(c.CbetTurn, c.CbetTurnOpportunities),
            StatRate.Of(c.CheckRaiseFlop, c.CheckRaiseFlopOpportunities),
            StatRate.Of(c.WonWhenSawFlop, c.SawFlop),
            StatRate.Of(c.PostflopAggressive, c.PostflopDecisions));
    }
}

/// <param name="Format">The table format the figures are for.</param>
/// <param name="HandsByFormat">Hands per format with the other filters: lets the page offer both.</param>
/// <param name="PendingHands">Hands whose facts are still being computed: figures are partial until 0.</param>
public sealed record StatisticsReport(
    StatLine Overall,
    IReadOnlyList<PositionStatLine> ByPosition,
    int PendingHands,
    StatisticsSample Sample,
    TableFormat Format,
    FormatCounts HandsByFormat);

/// <param name="Position">Null when the position could not be named.</param>
public sealed record PositionStatLine(PokerPosition? Position, StatLine Line);

public sealed record PhaseStatLine(TournamentPhase Phase, StatLine Line);

/// <param name="Month">First day of the month (UTC).</param>
public sealed record MonthStatLine(DateOnly Month, StatLine Line);

/// <summary>The same statistics split by tournament phase and by month, within one filter.</summary>
/// <param name="References">Overall reference ranges (ADR-0007) by statistic: the band drawn under a trend.</param>
public sealed record StatisticsBreakdowns(
    TableFormat Format,
    IReadOnlyList<PhaseStatLine> ByPhase,
    IReadOnlyList<MonthStatLine> ByMonth,
    IReadOnlyList<ReferenceRange> References);

public sealed class StatisticsService(IStatisticsReadStore store)
{
    private static readonly PokerPosition[] Order =
    [
        PokerPosition.Utg, PokerPosition.Utg1, PokerPosition.Utg2, PokerPosition.Lojack, PokerPosition.Hijack,
        PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind, PokerPosition.BigBlind,
    ];

    /// <param name="filter">Without a format, the one the player plays most is used.</param>
    public async Task<StatisticsReport> GetAsync(Guid userId, StatisticsFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var formats = await store.CountByFormatAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var format = filter.Format ?? formats.Busiest;
        filter = filter with { Format = format };
        var groups = await store.CountByPositionAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var pending = await store.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);
        var sample = await store.CountTournamentsAsync(userId, filter, HeroHandFacts.Version, cancellationToken);

        var overall = groups.Aggregate(HeroStatCounts.Zero, (total, g) => total.Add(g.Counts));
        var byPosition = groups
            .OrderBy(g => g.Position is { } p ? Array.IndexOf(Order, p) : int.MaxValue)
            .Select(g => new PositionStatLine(g.Position, StatLine.From(g.Counts)))
            .ToList();
        return new StatisticsReport(StatLine.From(overall), byPosition, pending, sample, format, formats);
    }

    /// <summary>Months shown when the filter has no start: a year is enough to see a trend.</summary>
    public const int TrendMonths = 12;

    /// <param name="filter">Its phase is ignored for the phases (all three are given); without a start, the
    /// trend covers the last <see cref="TrendMonths"/> months.</param>
    public async Task<StatisticsBreakdowns> GetBreakdownsAsync(Guid userId, StatisticsFilter filter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var format = filter.Format ?? (await store.CountByFormatAsync(userId, filter, HeroHandFacts.Version, cancellationToken)).Busiest;
        filter = filter with { Format = format };

        var phases = new List<PhaseStatLine>();
        foreach (var phase in Enum.GetValues<TournamentPhase>())
        {
            var groups = await store.CountByPositionAsync(userId, filter with { Phase = phase }, HeroHandFacts.Version, cancellationToken);
            phases.Add(new PhaseStatLine(phase, StatLine.From(groups.Aggregate(HeroStatCounts.Zero, (total, g) => total.Add(g.Counts)))));
        }

        var firstMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-(TrendMonths - 1));
        var months = await store.CountByMonthAsync(userId, filter with { From = filter.From ?? firstMonth }, HeroHandFacts.Version, cancellationToken);

        return new StatisticsBreakdowns(
            format,
            phases,
            months.Select(m => new MonthStatLine(m.Month, StatLine.From(m.Counts))).ToList(),
            ReferenceRanges.LowStakesMtt.Where(r => r.Position is null).ToList());
    }
}
