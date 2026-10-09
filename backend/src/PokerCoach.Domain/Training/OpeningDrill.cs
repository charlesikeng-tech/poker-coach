using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Domain.Training;

/// <summary>What the player answers in an open-or-fold drill. Values travel in the API and are stored.</summary>
public enum DrillAnswer
{
    Fold = 0,
    Raise = 1,
}

/// <summary>One thing to learn: a starting hand from a seat, at a format and stack band.</summary>
public sealed record DrillItem(TableFormat Format, StackBand Band, PokerPosition Position, HandClass Hand);

/// <summary>A dealt spot: the item with concrete cards and stack, as shown at the table.</summary>
public sealed record OpeningSpot(DrillItem Item, Card First, Card Second, decimal StackBigBlinds);

/// <summary>
/// Open-or-fold drills (ADR-0009, block 3): folded to the hero, raise first in or fold. The answer key is
/// the reference opening range (version 1): the drill teaches our references, it does not pretend to be
/// GTO. Half the hands are drawn near the edge of the range, where decisions are actually learned; the
/// other half as they come at the table (by number of combinations), so obvious folds still appear.
/// </summary>
public static class OpeningDrill
{
    public const double BoundaryShare = 0.5;

    public static DrillAnswer Expected(DrillItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var range = ReferenceOpeningRanges.For(item.Band, item.Format, item.Position)
            ?? throw new ArgumentException("This seat has no opening range.", nameof(item));
        return range.Contains(item.Hand) ? DrillAnswer.Raise : DrillAnswer.Fold;
    }

    /// <summary>
    /// A new spot. <paramref name="review"/> (a hand missed before) is asked again as is; otherwise the seat
    /// is drawn from <paramref name="seats"/> (repeat a seat to weight it) and the hand as described above.
    /// </summary>
    public static OpeningSpot Deal(Random random, TableFormat format, StackBand band, IReadOnlyList<PokerPosition> seats, DrillItem? review = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(seats);

        var item = review;
        if (item is null)
        {
            if (seats.Count == 0)
            {
                throw new ArgumentException("At least one seat is needed.", nameof(seats));
            }

            var position = seats[random.Next(seats.Count)];
            var range = ReferenceOpeningRanges.For(band, format, position)
                ?? throw new ArgumentException($"{position} has no opening range.", nameof(seats));
            var boundary = Boundary(range);
            var hand = random.NextDouble() < BoundaryShare && boundary.Count > 0
                ? boundary[random.Next(boundary.Count)]
                : ByCombinations(random);
            item = new DrillItem(format, band, position, hand);
        }

        var (first, second) = Cards(random, item.Hand);
        return new OpeningSpot(item, first, second, Stack(random, item.Band));
    }

    /// <summary>Hands next to the edge of the range: in it with a neighbour out of it, or the reverse.</summary>
    public static IReadOnlyList<HandClass> Boundary(IReadOnlySet<HandClass> range)
    {
        ArgumentNullException.ThrowIfNull(range);
        var all = HandClass.All;
        bool In(int row, int column) => range.Contains(all[(row * 13) + column]);

        var boundary = new List<HandClass>();
        foreach (var hand in all)
        {
            var (row, column) = hand.GridCell;
            var inRange = In(row, column);
            var neighbours = new[] { (row - 1, column), (row + 1, column), (row, column - 1), (row, column + 1) }
                .Where(n => n.Item1 is >= 0 and < 13 && n.Item2 is >= 0 and < 13);
            if (neighbours.Any(n => In(n.Item1, n.Item2) != inRange))
            {
                boundary.Add(hand);
            }
        }

        return boundary;
    }

    private static HandClass ByCombinations(Random random)
    {
        var pick = random.Next(1326);
        foreach (var hand in HandClass.All)
        {
            pick -= hand.Combinations;
            if (pick < 0)
            {
                return hand;
            }
        }

        return HandClass.All[^1];
    }

    private static (Card, Card) Cards(Random random, HandClass hand)
    {
        var first = (Suit)random.Next(4);
        var second = hand.Suited ? first : (Suit)((((int)first) + 1 + random.Next(3)) % 4);
        return (new Card(hand.High, first), new Card(hand.Low, second));
    }

    /// <summary>A stack inside the band, by half big blinds.</summary>
    private static decimal Stack(Random random, StackBand band)
    {
        var (min, max) = band switch
        {
            StackBand.Short => (16, 24),
            StackBand.Mid => (26, 39),
            _ => (42, 90),
        };
        return min + (random.Next((max - min) * 2) / 2m);
    }
}
