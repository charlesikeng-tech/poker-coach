using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Ranges;

/// <summary>One starting hand facing an open: what the player did, what the reference does (null without one).</summary>
public sealed record DefenceCell(HandClass Hand, int Dealt, int Calls, int ThreeBets, DefenceAction? Reference);

/// <summary>A defender against an opener at the format: counts, and whether a reference is written for it.</summary>
public sealed record DefencePair(PokerPosition Defender, PokerPosition Opener, int Dealt, int Calls, int ThreeBets, bool HasReference)
{
    /// <summary>Calls and 3-bets over spots: how often the player kept playing.</summary>
    public StatRate DefenceRate => StatRate.Of(Calls + ThreeBets, Dealt);

    public StatRate ThreeBetRate => StatRate.Of(ThreeBets, Dealt);
}

/// <param name="CallShare">Share of all two-card holdings the reference calls; null without a reference.</param>
/// <param name="Cells">The 169 starting hands in grid order.</param>
public sealed record DefenceSpot(
    DefencePair Pair,
    string? CallNotation,
    string? ThreeBetNotation,
    decimal? CallShare,
    decimal? ThreeBetShare,
    IReadOnlyList<DefenceCell> Cells);

/// <param name="SpotsByFormat">Spots facing an open in each format, for the band and period.</param>
/// <param name="Pairs">Every defender–opener pair of the format, opener first to act first, even without spots.</param>
/// <param name="Selected">The pair asked for, or the one worth showing first.</param>
public sealed record DefenceRanges(
    TableFormat Format,
    StackBand Band,
    IReadOnlyDictionary<TableFormat, int> SpotsByFormat,
    IReadOnlyList<DefencePair> Pairs,
    DefenceSpot Selected,
    int PendingHands,
    int ReferenceVersion);

/// <summary>
/// The player's defence against a single open next to the reference (ADR-0018): for a defender and an opener,
/// how often each starting hand was called, 3-bet or folded. Only counts, never extrapolated.
/// </summary>
public sealed class DefenceRangeService(IRangeReadStore ranges, IStatisticsReadStore statistics)
{
    /// <param name="band">Short, mid or deep; push/fold has no open to defend against.</param>
    /// <param name="defender">Null, or not a valid pair with <paramref name="opener"/>: the big blind's busiest spot.</param>
    public async Task<DefenceRanges> GetAsync(
        Guid userId,
        TableFormat? format,
        StackBand band,
        PokerPosition? defender,
        PokerPosition? opener,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken,
        GameType? gameType = null)
    {
        if (band == StackBand.Push)
        {
            throw new ArgumentOutOfRangeException(nameof(band), "Push/fold has no open to defend against.");
        }

        var (min, max) = ReferenceOpeningRanges.Bounds(band);
        var counts = await ranges.CountDefencesAsync(userId, new StatisticsFilter(from, to, min > 0 ? min : null, max, GameType: gameType), HeroHandFacts.Version, cancellationToken);
        var pending = await statistics.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var spots = Enum.GetValues<TableFormat>().ToDictionary(
            f => f,
            f => counts.Where(c => TableFormats.Of(c.MaxSeats) == f).Sum(c => c.Dealt));
        var shown = format ?? (spots[TableFormat.FullRing] > spots[TableFormat.SixMax] ? TableFormat.FullRing : TableFormat.SixMax);

        var cellsByPair = Cells(shown, counts);
        var pairs = Pairs(shown)
            .Select(p =>
            {
                var cells = cellsByPair.GetValueOrDefault(p) ?? [];
                return new DefencePair(
                    p.Defender,
                    p.Opener,
                    cells.Sum(c => c.Value.Dealt),
                    cells.Sum(c => c.Value.Calls),
                    cells.Sum(c => c.Value.ThreeBets),
                    ReferenceDefenceRanges.For(band, shown, p.Defender, p.Opener) is not null);
            })
            .ToList();

        var bigBlind = pairs.Where(p => p.Defender == PokerPosition.BigBlind).ToList();
        var selected = pairs.FirstOrDefault(p => p.Defender == defender && p.Opener == opener)
            ?? (bigBlind.MaxBy(p => p.Dealt) is { Dealt: > 0 } busiest ? busiest : bigBlind.First(p => p.Opener == PokerPosition.Button));

        return new DefenceRanges(
            shown,
            band,
            spots,
            pairs,
            Spot(band, shown, selected, cellsByPair.GetValueOrDefault((selected.Defender, selected.Opener))),
            pending,
            ReferenceDefenceRanges.Version);
    }

    /// <summary>Openers first to act first, each with the seats behind it that can face its open.</summary>
    internal static IEnumerable<(PokerPosition Defender, PokerPosition Opener)> Pairs(TableFormat format)
    {
        var seats = ReferenceOpeningRanges.Positions(format).Append(PokerPosition.BigBlind).ToList();
        for (var o = 0; o < seats.Count - 1; o++)
        {
            for (var d = o + 1; d < seats.Count; d++)
            {
                yield return (seats[d], seats[o]);
            }
        }
    }

    private static Dictionary<(PokerPosition Defender, PokerPosition Opener), Dictionary<HandClass, (int Dealt, int Calls, int ThreeBets)>> Cells(
        TableFormat format,
        IEnumerable<DefenceHoldingCount> counts) =>
        counts
            .Where(c => TableFormats.Of(c.MaxSeats) == format)
            .Select(c => (Count: c, Ok: HandClass.TryParseHoldings(c.HeroCards, out var hand), Hand: hand))
            // Unreadable hero cards are left out rather than guessed.
            .Where(x => x.Ok)
            .GroupBy(x => (
                OpeningSeat.Canonical(x.Count.Position, x.Count.PlayersDealt, format),
                OpeningSeat.Canonical(x.Count.Opener, x.Count.PlayersDealt, format)))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.Hand).ToDictionary(
                    h => h.Key,
                    h => (h.Sum(x => x.Count.Dealt), h.Sum(x => x.Count.Calls), h.Sum(x => x.Count.ThreeBets))));

    private static DefenceSpot Spot(StackBand band, TableFormat format, DefencePair pair, Dictionary<HandClass, (int Dealt, int Calls, int ThreeBets)>? counts)
    {
        var reference = ReferenceDefenceRanges.For(band, format, pair.Defender, pair.Opener);
        var cells = HandClass.All
            .Select(hand =>
            {
                var c = counts?.GetValueOrDefault(hand) ?? default;
                return new DefenceCell(hand, c.Dealt, c.Calls, c.ThreeBets, reference?.ActionFor(hand));
            })
            .ToList();
        return new DefenceSpot(
            pair,
            reference?.CallNotation,
            reference?.ThreeBetNotation,
            reference is null ? null : ReferenceOpeningRanges.ComboShare(reference.Call),
            reference is null ? null : ReferenceOpeningRanges.ComboShare(reference.ThreeBet),
            cells);
    }
}
