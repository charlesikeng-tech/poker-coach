using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Domain.Training;

/// <summary>
/// One sizing question: open from a seat when folded to, or 3-bet a single open, at a stack (every stack equal,
/// so it is also the effective stack). The hand is not part of it: the reference uses one size per spot.
/// </summary>
/// <param name="Opener">3-bet drills: the seat that opened; null for an open drill.</param>
/// <param name="OpenToBigBlinds">3-bet drills: the open faced, as a raise-to in big blinds.</param>
public sealed record SizingItem(
    TableFormat Format,
    StackBand Band,
    PokerPosition Position,
    decimal StackBigBlinds,
    PokerPosition? Opener = null,
    decimal? OpenToBigBlinds = null)
{
    public SizingKind Kind => Opener is null ? SizingKind.Open : SizingKind.ThreeBet;
}

/// <summary>A dealt sizing spot: the item and a hand the reference plays there, as shown at the table.</summary>
public sealed record SizingSpot(SizingItem Item, HandClass Hand, Card First, Card Second);

/// <summary>
/// Sizing drills (ADR-0020): the decision to raise is given (the hand comes from the reference range of the spot),
/// the question is how much. The answer key is <see cref="ReferenceSizings"/>; any size inside the reference
/// interval is right.
/// </summary>
public static class SizingDrill
{
    /// <summary>Open sizes faced in 3-bet drills, as raise-to in big blinds; the small blind opens bigger.</summary>
    private static readonly decimal[] OpensFaced = [2m, 2.2m, 2.5m];

    private static readonly decimal[] SmallBlindOpensFaced = [2.5m, 3m];

    public static IReadOnlyList<RaiseSize> Choices(SizingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind == SizingKind.Open ? ReferenceSizings.OpenChoices : ReferenceSizings.ThreeBetChoices;
    }

    public static SizingAdvice Advice(SizingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind == SizingKind.Open
            ? ReferenceSizings.Open(item.Position, item.StackBigBlinds)
            : ReferenceSizings.ThreeBet(item.Position, item.Opener!.Value, item.OpenToBigBlinds!.Value, item.StackBigBlinds);
    }

    /// <summary>Whether an answer is one of the offered sizes; anything else is a malformed request, not a wrong answer.</summary>
    public static bool IsOffered(SizingItem item, RaiseSize answer) => Choices(item).Contains(answer);

    public static bool IsCorrect(SizingItem item, RaiseSize answer) => Advice(item).Judge(answer) == SizingVerdict.InLine;

    /// <summary>Defender–opener pairs drilled at a format: those with a written 3-bet range to draw hands from.</summary>
    public static IReadOnlyList<(PokerPosition Defender, PokerPosition Opener)> ThreeBetPairs(TableFormat format)
    {
        var seats = ReferenceOpeningRanges.Positions(format).Append(PokerPosition.BigBlind).ToList();
        var pairs = new List<(PokerPosition, PokerPosition)>();
        for (var o = 0; o < seats.Count - 1; o++)
        {
            for (var d = o + 1; d < seats.Count; d++)
            {
                if (ThreeBetRange(format, seats[d], seats[o]) is { Count: > 0 })
                {
                    pairs.Add((seats[d], seats[o]));
                }
            }
        }

        return pairs;
    }

    /// <summary>
    /// A new spot: <paramref name="review"/> as is (with a new hand), or an open or a 3-bet (<paramref name="kind"/>,
    /// or either when null) drawn at <paramref name="band"/>.
    /// </summary>
    public static SizingSpot Deal(Random random, TableFormat format, StackBand band, SizingKind? kind, SizingItem? review = null, StakesLevel stakesLevel = default)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (band == StackBand.Push)
        {
            throw new ArgumentOutOfRangeException(nameof(band), "Push/fold has one size: all-in.");
        }

        var item = review ?? NewItem(random, format, band, kind ?? (random.Next(2) == 0 ? SizingKind.Open : SizingKind.ThreeBet));
        var range = item.Kind == SizingKind.Open
            ? ReferenceOpeningRanges.For(item.Band, item.Format, item.Position, stakesLevel: stakesLevel)
            : ThreeBetRange(item.Format, item.Position, item.Opener!.Value);
        var hands = range is { Count: > 0 } ? range.ToList() : [.. HandClass.All];
        var hand = hands[random.Next(hands.Count)];
        var (first, second) = OpeningDrill.Cards(random, hand);
        return new SizingSpot(item, hand, first, second);
    }

    private static SizingItem NewItem(Random random, TableFormat format, StackBand band, SizingKind kind)
    {
        var stack = OpeningDrill.Stack(random, band);
        if (kind == SizingKind.Open)
        {
            var seats = ReferenceOpeningRanges.Positions(format);
            return new SizingItem(format, band, seats[random.Next(seats.Count)], stack);
        }

        var pairs = ThreeBetPairs(format);
        var (defender, opener) = pairs[random.Next(pairs.Count)];
        var faced = opener == PokerPosition.SmallBlind ? SmallBlindOpensFaced : OpensFaced;
        return new SizingItem(format, band, defender, stack, opener, faced[random.Next(faced.Length)]);
    }

    /// <summary>The written 3-bet range of the pair; below 25 BB (none written) the 25 BB one stands in for the hand shown.</summary>
    private static IReadOnlySet<HandClass>? ThreeBetRange(TableFormat format, PokerPosition defender, PokerPosition opener) =>
        ReferenceDefenceRanges.For(StackBand.Mid, format, defender, opener)?.ThreeBet;
}
