using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Leaks;

/// <param name="Format">The table format analysed: references by position differ between 6-max and full ring.</param>
/// <param name="HandsByFormat">Hands per format (15+ BB): lets the page offer the other one.</param>
/// <param name="Hands">Hands the detection looked at (15+ big blinds, that format).</param>
/// <param name="PendingHands">Hands still being analyzed: the result may change once they are.</param>
public sealed record LeakAnalysis(
    LeakReport Report,
    int Hands,
    StatisticsSample Sample,
    int PendingHands,
    int ReferenceVersion,
    TableFormat Format,
    FormatCounts HandsByFormat);

/// <summary>
/// Leak detection (ADR-0007): the hero's statistics against reference ranges, with a sample guard, one
/// table format at a time. Position references are written for full-ring seats: a 6-max seat is judged
/// against the full-ring seat at the same distance from the button, and reported under its 6-max name.
/// </summary>
/// <summary>The hero's counts over a period, by reference seat, ready to measure any statistic.</summary>
/// <param name="ByPosition">Keyed by the full-ring reference seat (see <see cref="OpeningSeat.FullRingEquivalent"/>).</param>
public sealed record LeakCounts(TableFormat Format, HeroStatCounts Overall, IReadOnlyDictionary<PokerPosition, HeroStatCounts> ByPosition)
{
    /// <param name="position">The seat as named at <see cref="Format"/>; null for the overall rate.</param>
    public (int Made, int Opportunities) Measure(LeakStat stat, PokerPosition? position)
    {
        var counts = position is { } p
            ? ByPosition.GetValueOrDefault(OpeningSeat.FullRingEquivalent(p, Format), HeroStatCounts.Zero)
            : Overall;
        return LeakDetector.Measure(stat, counts);
    }
}

public sealed class LeakService(IStatisticsReadStore store)
{
    /// <summary>Counts of one format over a period (15+ BB hands), for measuring statistics outside a full analysis.</summary>
    /// <param name="to">Exclusive upper bound.</param>
    public async Task<LeakCounts> CountAsync(Guid userId, TableFormat format, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var filter = new StatisticsFilter(from, to, ReferenceRanges.MinStackBigBlinds, null, Format: format);
        var groups = await store.CountByPositionAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var (overall, byPosition) = Aggregate(groups, format);
        return new LeakCounts(format, overall, byPosition);
    }

    /// <param name="format">Null: the format the player plays most.</param>
    /// <param name="to">Exclusive upper bound.</param>
    public async Task<LeakAnalysis> GetAsync(
        Guid userId,
        TableFormat? format,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var filter = new StatisticsFilter(from, to, ReferenceRanges.MinStackBigBlinds, null);
        var formats = await store.CountByFormatAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var shown = format ?? formats.Busiest;
        filter = filter with { Format = shown };

        var groups = await store.CountByPositionAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var sample = await store.CountTournamentsAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var pending = await store.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var (overall, byPosition) = Aggregate(groups, shown);

        var report = LeakDetector.Detect(overall, byPosition, ReferenceRanges.LowStakesMtt);
        return new LeakAnalysis(
            new LeakReport(
                report.Leaks.Select(l => l with { Position = Named(l.Position, shown) }).ToList(),
                report.UnderSampled.Select(u => u with { Position = Named(u.Position, shown) }).ToList()),
            overall.Hands,
            sample,
            pending,
            ReferenceRanges.Version,
            shown,
            formats);
    }

    private static (HeroStatCounts Overall, Dictionary<PokerPosition, HeroStatCounts> ByPosition) Aggregate(
        IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)> groups,
        TableFormat format)
    {
        var overall = groups.Aggregate(HeroStatCounts.Zero, (total, g) => total.Add(g.Counts));
        var byPosition = groups
            .Where(g => g.Position is not null)
            .GroupBy(g => OpeningSeat.FullRingEquivalent(g.Position!.Value, format))
            .ToDictionary(g => g.Key, g => g.Aggregate(HeroStatCounts.Zero, (total, x) => total.Add(x.Counts)));
        return (overall, byPosition);
    }

    /// <summary>Back from the full-ring reference seat to the seat's name at this format.</summary>
    private static PokerPosition? Named(PokerPosition? position, TableFormat format) =>
        format == TableFormat.SixMax && position == PokerPosition.Lojack ? PokerPosition.Utg : position;
}
