using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Ranges;

/// <summary>One starting hand facing a 3-bet: what the player did, what the reference does (null without one).</summary>
public sealed record VsThreeBetCell(HandClass Hand, int Dealt, int Calls, int FourBets, VsThreeBetAction? Reference);

/// <summary>An opener against a 3-bettor at the format: counts, and whether a reference is written for it.</summary>
public sealed record VsThreeBetPair(PokerPosition Opener, PokerPosition ThreeBettor, int Dealt, int Calls, int FourBets, bool HasReference)
{
    public StatRate ContinueRate => StatRate.Of(Calls + FourBets, Dealt);

    public StatRate FourBetRate => StatRate.Of(FourBets, Dealt);
}

/// <param name="CallShare">Share of all two-card holdings the reference calls; null without a reference.</param>
/// <param name="Cells">The 169 starting hands in grid order.</param>
public sealed record VsThreeBetSpot(
    VsThreeBetPair Pair,
    string? CallNotation,
    string? FourBetNotation,
    decimal? CallShare,
    decimal? FourBetShare,
    IReadOnlyList<VsThreeBetCell> Cells);

/// <param name="SpotsByFormat">Spots facing a 3-bet in each format, for the band and period.</param>
/// <param name="Pairs">Every opener–3-bettor pair of the format, opener first to act first.</param>
/// <param name="Selected">The pair asked for, or the one worth showing first.</param>
public sealed record VsThreeBetRanges(
    TableFormat Format,
    StackBand Band,
    IReadOnlyDictionary<TableFormat, int> SpotsByFormat,
    IReadOnlyList<VsThreeBetPair> Pairs,
    VsThreeBetSpot Selected,
    int PendingHands,
    int ReferenceVersion);

/// <summary>
/// The player's reaction to a 3-bet after opening: for an opener and a 3-bettor, how often each
/// starting hand was called, 4-bet or folded. Only counts, never extrapolated.
/// </summary>
public sealed class VsThreeBetRangeService(IRangeReadStore ranges, IStatisticsReadStore statistics)
{
    /// <param name="band">Short, mid or deep; push/fold has no open to 3-bet against.</param>
    /// <param name="opener">Null, or not a valid pair with <paramref name="threeBettor"/>: picks the best default.</param>
    public async Task<VsThreeBetRanges> GetAsync(
        Guid userId,
        TableFormat? format,
        StackBand band,
        PokerPosition? opener,
        PokerPosition? threeBettor,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken,
        GameType? gameType = null)
    {
        if (band == StackBand.Push)
        {
            throw new ArgumentOutOfRangeException(nameof(band), "Push/fold has no open to 3-bet against.");
        }

        var (min, max) = ReferenceOpeningRanges.Bounds(band);
        var baseFilter = new StatisticsFilter(from, to, min > 0 ? min : null, max, GameType: gameType);

        var pairCounts = await ranges.CountVsThreeBetPairsAsync(userId, baseFilter, HeroHandFacts.Version, cancellationToken);
        var pending = await statistics.CountPendingAsync(userId, HeroHandFacts.Version, cancellationToken);

        var spots = Enum.GetValues<TableFormat>().ToDictionary(
            f => f,
            f => pairCounts.Where(c => TableFormats.Of(c.MaxSeats) == f).Sum(c => c.Dealt));
        var shown = format ?? (spots[TableFormat.FullRing] > spots[TableFormat.SixMax] ? TableFormat.FullRing : TableFormat.SixMax);

        var pairTotals = pairCounts
            .Where(c => TableFormats.Of(c.MaxSeats) == shown)
            .GroupBy(c => (
                OpeningSeat.Canonical(c.Opener, c.PlayersDealt, shown),
                OpeningSeat.Canonical(c.ThreeBettor, c.PlayersDealt, shown)))
            .ToDictionary(
                g => g.Key,
                g => (Dealt: g.Sum(x => x.Dealt), Calls: g.Sum(x => x.Calls), FourBets: g.Sum(x => x.FourBets)));

        var pairs = Pairs(shown)
            .Select(p =>
            {
                var (dealt, calls, fourBets) = pairTotals.GetValueOrDefault(p);
                return new VsThreeBetPair(
                    p.Opener,
                    p.ThreeBettor,
                    dealt,
                    calls,
                    fourBets,
                    ReferenceVsThreeBetRanges.For(band, shown, p.Opener, p.ThreeBettor) is not null);
            })
            .ToList();

        var selected = pairs.FirstOrDefault(p => p.Opener == opener && p.ThreeBettor == threeBettor)
            ?? (opener is not null ? pairs.Where(p => p.Opener == opener).MaxBy(p => p.Dealt) : null)
            ?? pairs.MaxBy(p => p.Dealt) ?? pairs.First();

        var holdingCounts = await ranges.CountVsThreeBetsAsync(userId, baseFilter with { Format = shown }, HeroHandFacts.Version, cancellationToken);
        var cellsByPair = Cells(shown, holdingCounts);

        return new VsThreeBetRanges(
            shown,
            band,
            spots,
            pairs,
            Spot(band, shown, selected, cellsByPair.GetValueOrDefault((selected.Opener, selected.ThreeBettor))),
            pending,
            ReferenceVsThreeBetRanges.Version);
    }

    /// <summary>Openers first to act first, each with the seats behind it that can 3-bet its open.</summary>
    internal static IEnumerable<(PokerPosition Opener, PokerPosition ThreeBettor)> Pairs(TableFormat format)
    {
        var seats = ReferenceOpeningRanges.Positions(format).Append(PokerPosition.BigBlind).ToList();
        for (var o = 0; o < seats.Count - 1; o++)
        {
            for (var tb = o + 1; tb < seats.Count; tb++)
            {
                yield return (seats[o], seats[tb]);
            }
        }
    }

    private static Dictionary<(PokerPosition Opener, PokerPosition ThreeBettor), Dictionary<HandClass, (int Dealt, int Calls, int FourBets)>> Cells(
        TableFormat format,
        IEnumerable<VsThreeBetHoldingCount> counts) =>
        counts
            .Where(c => TableFormats.Of(c.MaxSeats) == format)
            .Select(c => (Count: c, Ok: HandClass.TryParseHoldings(c.HeroCards, out var hand), Hand: hand))
            .Where(x => x.Ok)
            .GroupBy(x => (
                OpeningSeat.Canonical(x.Count.Opener, x.Count.PlayersDealt, format),
                OpeningSeat.Canonical(x.Count.ThreeBettor, x.Count.PlayersDealt, format)))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.Hand).ToDictionary(
                    h => h.Key,
                    h => (h.Sum(x => x.Count.Dealt), h.Sum(x => x.Count.Calls), h.Sum(x => x.Count.FourBets))));

    private static VsThreeBetSpot Spot(
        StackBand band, TableFormat format, VsThreeBetPair pair,
        Dictionary<HandClass, (int Dealt, int Calls, int FourBets)>? counts)
    {
        var reference = ReferenceVsThreeBetRanges.For(band, format, pair.Opener, pair.ThreeBettor);
        var cells = HandClass.All
            .Select(hand =>
            {
                var c = counts?.GetValueOrDefault(hand) ?? default;
                return new VsThreeBetCell(hand, c.Dealt, c.Calls, c.FourBets, reference?.ActionFor(hand));
            })
            .ToList();
        return new VsThreeBetSpot(
            pair,
            reference?.CallNotation,
            reference?.FourBetNotation,
            reference is null ? null : ReferenceOpeningRanges.ComboShare(reference.Call),
            reference is null ? null : ReferenceOpeningRanges.ComboShare(reference.FourBet),
            cells);
    }
}
