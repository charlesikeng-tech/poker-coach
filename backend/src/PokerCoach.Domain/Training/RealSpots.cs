using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Domain.Training;

/// <summary>What the hero actually did when it was folded to him.</summary>
public enum RealAction
{
    Fold,
    Raise,

    /// <summary>Called first in: never the reference's answer (it raises or folds).</summary>
    Limp,
}

/// <summary>One real raise-first-in spot of the hero, as stored in his facts.</summary>
/// <param name="HeroCards">"AhKd".</param>
public sealed record RealOpeningRow(
    Guid HandId,
    DateTimeOffset PlayedAt,
    PokerPosition Position,
    int PlayersDealt,
    decimal StackBigBlinds,
    string HeroCards,
    bool Rfi,
    bool Limp);

/// <summary>A real spot turned into a drill: the same question, the hero's real answer beside the key.</summary>
public sealed record RealSpot(Guid HandId, DateTimeOffset PlayedAt, OpeningSpot Spot, RealAction Actual, DrillAnswer Expected)
{
    /// <summary>The hero's real decision differed from the answer key.</summary>
    public bool Missed => Actual switch
    {
        RealAction.Raise => Expected != DrillAnswer.Raise,
        RealAction.Fold => Expected != DrillAnswer.Fold,
        _ => true,
    };
}

/// <summary>
/// The quiz on the player's own hands: his real open-or-fold spots, judged by the same answer key as the
/// drills (reference ranges above 15 BB, push/fold equilibrium below), asked again where he went wrong.
/// </summary>
public static class RealSpots
{
    /// <returns>Null when the spot cannot be judged: a seat without reference, unreadable cards.</returns>
    public static RealSpot? From(RealOpeningRow row, TableFormat format)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.HeroCards.Length != 4
            || !Card.TryParse(row.HeroCards[..2], out var first)
            || !Card.TryParse(row.HeroCards[2..], out var second)
            || first == second)
        {
            return null;
        }

        var seat = OpeningSeat.Canonical(row.Position, row.PlayersDealt, format);
        if (!ReferenceOpeningRanges.Positions(format).Contains(seat))
        {
            return null;
        }

        var band = BandOf(row.StackBigBlinds);
        int? pushStack = band == StackBand.Push
            ? Math.Clamp((int)Math.Round(row.StackBigBlinds, MidpointRounding.AwayFromZero), PushFoldNash.MinStack, PushFoldNash.MaxStack)
            : null;
        var item = new DrillItem(format, band, seat, HandClass.Of(first, second), pushStack);
        var actual = row.Rfi ? RealAction.Raise : row.Limp ? RealAction.Limp : RealAction.Fold;
        return new RealSpot(row.HandId, row.PlayedAt, new OpeningSpot(item, first, second, row.StackBigBlinds), actual, OpeningDrill.Expected(item));
    }

    public static StackBand BandOf(decimal stackBigBlinds) =>
        Enum.GetValues<StackBand>().First(band =>
        {
            var (min, max) = ReferenceOpeningRanges.Bounds(band);
            return stackBigBlinds >= min && (max is null || stackBigBlinds < max);
        });
}
