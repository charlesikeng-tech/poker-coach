using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Ranges;

/// <summary>Raise-first-in spots of one position, table and exact holding ("AhKd").</summary>
/// <param name="MaxSeats">Seats at the table: its format (6-max, full ring).</param>
/// <param name="PlayersDealt">Players dealt in: with the position, it tells how many are left to act.</param>
/// <param name="Dealt">Times the hero had this holding when folded to (an RFI opportunity).</param>
/// <param name="Opens">Times he raised first in.</param>
/// <param name="Limps">Times he called first in.</param>
public sealed record OpeningHoldingCount(PokerPosition Position, int MaxSeats, int PlayersDealt, string HeroCards, int Dealt, int Opens, int Limps);

public interface IRangeReadStore
{
    /// <summary>RFI spots of the user's analysed hands within the filter, grouped by position, table size and holding.</summary>
    Task<IReadOnlyList<OpeningHoldingCount>> CountOpeningsAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken);
}

/// <summary>One starting hand: what the player did with it, and whether the reference opens it.</summary>
/// <param name="InReference">The reference range of the position and band raises it first in.</param>
public sealed record RangeCell(HandClass Hand, int Dealt, int Opens, int Limps, bool InReference);

/// <param name="Position">Seat by distance to the button within the format (see <see cref="OpeningSeat"/>).</param>
/// <param name="OpenRate">Opens over RFI spots: the position's opening frequency.</param>
/// <param name="ReferenceRate">The position's reference opening rate (ADR-0007), when one exists.</param>
/// <param name="ReferenceNotation">The reference range as written ("22+, A2s+, …").</param>
/// <param name="ReferenceShare">Share of all two-card holdings the reference range opens.</param>
/// <param name="Cells">The 169 starting hands, in grid order.</param>
public sealed record PositionRange(
    PokerPosition Position,
    int Dealt,
    int Opens,
    int Limps,
    StatRate OpenRate,
    ReferenceRange? ReferenceRate,
    string? ReferenceNotation,
    decimal? ReferenceShare,
    IReadOnlyList<RangeCell> Cells);

/// <param name="Format">The table format shown.</param>
/// <param name="SpotsByFormat">RFI spots in each format for the band and period: lets the page offer the
/// other format, and pick the one the player actually plays.</param>
/// <param name="Positions">Every position that opens at that format, from UTG to the small blind, even
/// without spots (the reference is still worth showing).</param>
/// <param name="PendingHands">Hands whose facts are still being computed: ranges are partial until 0.</param>
/// <param name="PushStack">For the push/fold band: the stack the computed reference is for.</param>
public sealed record OpeningRanges(
    TableFormat Format,
    StackBand Band,
    int? PushStack,
    IReadOnlyDictionary<TableFormat, int> SpotsByFormat,
    IReadOnlyList<PositionRange> Positions,
    int PendingHands,
    int ReferenceVersion);

/// <summary>
/// The player's actual opening ranges next to the reference ones (ADR-0009, blocks 1 and 2): for each
/// position, how often each of the 169 starting hands is raised first in, and whether the reference opens
/// it. Only counts, never extrapolated.
/// </summary>
public sealed class RangeService(IRangeReadStore ranges, IStatisticsReadStore statistics)
{
    /// <param name="format">Null: the format with the most spots (6-max on a tie or without hands).</param>
    public async Task<OpeningRanges> GetOpeningAsync(
        Guid userId,
        TableFormat? format,
        StackBand band,
        int? pushStack,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var (min, max) = ReferenceOpeningRanges.Bounds(band);
        var counts = await ranges.CountOpeningsAsync(userId, new StatisticsFilter(from, to, min > 0 ? min : null, max), HeroHandFacts.Version, cancellationToken);
        var pending = await statistics.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var spots = Enum.GetValues<TableFormat>().ToDictionary(
            f => f,
            f => counts.Where(c => TableFormats.Of(c.MaxSeats) == f).Sum(c => c.Dealt));
        var shown = format ?? (spots[TableFormat.FullRing] > spots[TableFormat.SixMax] ? TableFormat.FullRing : TableFormat.SixMax);
        var stack = band == StackBand.Push ? pushStack ?? ReferenceOpeningRanges.DefaultPushStack : (int?)null;
        return new OpeningRanges(shown, band, stack, spots, Build(shown, band, counts, stack), pending, ReferenceOpeningRanges.Version);
    }

    internal static List<PositionRange> Build(TableFormat format, StackBand band, IEnumerable<OpeningHoldingCount> counts, int? pushStack = null)
    {
        var byPosition = counts
            .Where(c => TableFormats.Of(c.MaxSeats) == format)
            .Select(c => (Count: c, Ok: HandClass.TryParseHoldings(c.HeroCards, out var hand), Hand: hand))
            // Unreadable hero cards are left out rather than guessed.
            .Where(x => x.Ok)
            .ToLookup(x => OpeningSeat.Canonical(x.Count.Position, x.Count.PlayersDealt, format));

        return ReferenceOpeningRanges.Positions(format)
            .Select(position =>
            {
                var perHand = byPosition[position]
                    .GroupBy(x => x.Hand)
                    .ToDictionary(
                        h => h.Key,
                        h => (Dealt: h.Sum(x => x.Count.Dealt), Opens: h.Sum(x => x.Count.Opens), Limps: h.Sum(x => x.Count.Limps)));
                var reference = ReferenceOpeningRanges.For(band, format, position, pushStack);
                var cells = HandClass.All
                    .Select(hand =>
                    {
                        var c = perHand.GetValueOrDefault(hand);
                        return new RangeCell(hand, c.Dealt, c.Opens, c.Limps, reference?.Contains(hand) ?? false);
                    })
                    .ToList();
                var dealt = cells.Sum(c => c.Dealt);
                var opens = cells.Sum(c => c.Opens);
                return new PositionRange(
                    position,
                    dealt,
                    opens,
                    cells.Sum(c => c.Limps),
                    StatRate.Of(opens, dealt),
                    ReferenceRanges.LowStakesMtt.FirstOrDefault(
                        r => r.Stat == LeakStat.Rfi && r.Position == OpeningSeat.FullRingEquivalent(position, format)),
                    ReferenceOpeningRanges.NotationFor(band, format, position),
                    reference is null ? null : ReferenceOpeningRanges.ComboShare(reference),
                    cells);
            })
            .ToList();
    }
}
