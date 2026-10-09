using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Domain.Training;

/// <summary>
/// Big blind defence against a shove (push/fold, below 15 BB): a seat shoves, everyone else folds, the
/// hero in the big blind calls or folds. The answer key is the call range of the push/fold equilibrium
/// (<see cref="PushFoldNash"/>): chip EV, every stack equal, no ICM — sound away from the money, too loose
/// on the bubble, which the page says.
/// </summary>
public static class DefenceDrill
{
    public const PokerPosition Defender = PokerPosition.BigBlind;

    /// <summary>Seats that can shove into the big blind at a format: every seat but the big blind.</summary>
    public static IReadOnlyList<PokerPosition> Shovers(TableFormat format) =>
        PushFoldNash.Seats(format).Where(s => s != Defender).ToList();

    /// <summary>What the big blind calls with against this shover at this stack.</summary>
    public static IReadOnlySet<HandClass> Range(DrillItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Shover is not { } shover || item.PushStack is not { } stack)
        {
            throw new ArgumentException("A defence drill needs the shover and the stack.", nameof(item));
        }

        return PushFoldNash.Solve(item.Format, stack).Call(item.Position, shover);
    }

    /// <summary>
    /// A new spot: <paramref name="review"/> as is, or a shover drawn from <paramref name="shovers"/> (repeat
    /// one to weight it), a stack between <see cref="OpeningDrill.PushStackMin"/> and
    /// <see cref="OpeningDrill.PushStackMax"/>, and half the hands near the edge of the call range.
    /// </summary>
    public static OpeningSpot Deal(Random random, TableFormat format, IReadOnlyList<PokerPosition> shovers, DrillItem? review = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(shovers);

        var item = review;
        if (item is null)
        {
            if (shovers.Count == 0)
            {
                throw new ArgumentException("At least one shover is needed.", nameof(shovers));
            }

            var shover = shovers[random.Next(shovers.Count)];
            var stack = random.Next(OpeningDrill.PushStackMin, OpeningDrill.PushStackMax + 1);
            var probe = new DrillItem(format, StackBand.Push, Defender, HandClass.All[0], stack, shover);
            var boundary = OpeningDrill.Boundary(Range(probe));
            var hand = random.NextDouble() < OpeningDrill.BoundaryShare && boundary.Count > 0
                ? boundary[random.Next(boundary.Count)]
                : OpeningDrill.ByCombinations(random);
            item = probe with { Hand = hand };
        }

        var (first, second) = OpeningDrill.Cards(random, item.Hand);
        return new OpeningSpot(item, first, second, item.PushStack!.Value);
    }
}
