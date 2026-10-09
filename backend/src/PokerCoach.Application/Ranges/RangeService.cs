using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Ranges;

/// <summary>Raise-first-in spots of one position for one exact holding ("AhKd").</summary>
/// <param name="Dealt">Times the hero had this holding when folded to (an RFI opportunity).</param>
/// <param name="Opens">Times he raised first in.</param>
/// <param name="Limps">Times he called first in.</param>
public sealed record OpeningHoldingCount(PokerPosition Position, string HeroCards, int Dealt, int Opens, int Limps);

public interface IRangeReadStore
{
    /// <summary>RFI spots of the user's analysed hands within the filter, grouped by position and holding.</summary>
    Task<IReadOnlyList<OpeningHoldingCount>> CountOpeningsAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken);
}

/// <summary>One starting hand of a position's actual range. All zero when never dealt in that spot.</summary>
public sealed record RangeCell(HandClass Hand, int Dealt, int Opens, int Limps);

/// <param name="OpenRate">Opens over RFI spots: the position's opening frequency.</param>
/// <param name="Reference">The position's reference opening rate (ADR-0007), when one exists.</param>
/// <param name="Cells">The 169 starting hands, in grid order.</param>
public sealed record PositionRange(
    PokerPosition Position,
    int Dealt,
    int Opens,
    int Limps,
    StatRate OpenRate,
    ReferenceRange? Reference,
    IReadOnlyList<RangeCell> Cells);

/// <param name="Positions">Positions with at least one RFI spot, from UTG to the small blind.</param>
/// <param name="PendingHands">Hands whose facts are still being computed: ranges are partial until 0.</param>
public sealed record OpeningRanges(IReadOnlyList<PositionRange> Positions, int PendingHands, int ReferenceVersion);

/// <summary>
/// The player's actual opening ranges (ADR-0009, block 1): for each position, how often each of the 169
/// starting hands is raised first in. Only counts, never extrapolated: a hand dealt twice shows "2".
/// </summary>
public sealed class RangeService(IRangeReadStore ranges, IStatisticsReadStore statistics)
{
    public async Task<OpeningRanges> GetOpeningAsync(Guid userId, StatisticsFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var counts = await ranges.CountOpeningsAsync(userId, filter, HeroHandFacts.Version, cancellationToken);
        var pending = await statistics.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);
        return new OpeningRanges(Build(counts), pending, ReferenceRanges.Version);
    }

    internal static List<PositionRange> Build(IEnumerable<OpeningHoldingCount> counts)
    {
        var byPosition = counts
            .Select(c => (Count: c, Ok: HandClass.TryParseHoldings(c.HeroCards, out var hand), Hand: hand))
            // Unreadable hero cards are left out rather than guessed.
            .Where(x => x.Ok)
            .GroupBy(x => x.Count.Position);

        return byPosition
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var perHand = g
                    .GroupBy(x => x.Hand)
                    .ToDictionary(
                        h => h.Key,
                        h => (Dealt: h.Sum(x => x.Count.Dealt), Opens: h.Sum(x => x.Count.Opens), Limps: h.Sum(x => x.Count.Limps)));
                var cells = HandClass.All
                    .Select(hand => perHand.TryGetValue(hand, out var c)
                        ? new RangeCell(hand, c.Dealt, c.Opens, c.Limps)
                        : new RangeCell(hand, 0, 0, 0))
                    .ToList();
                var dealt = cells.Sum(c => c.Dealt);
                var opens = cells.Sum(c => c.Opens);
                return new PositionRange(
                    g.Key,
                    dealt,
                    opens,
                    cells.Sum(c => c.Limps),
                    StatRate.Of(opens, dealt),
                    ReferenceRanges.LowStakesMtt.FirstOrDefault(r => r.Stat == LeakStat.Rfi && r.Position == g.Key),
                    cells);
            })
            .Where(p => p.Dealt > 0)
            .ToList();
    }
}
