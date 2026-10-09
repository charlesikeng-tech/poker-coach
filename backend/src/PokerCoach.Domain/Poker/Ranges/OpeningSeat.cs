using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>
/// Opening ranges depend on how many players are left to act, not on the seat's name. Seats before the
/// hijack are renamed by their distance to the button within the table's format: at a 6-max table the
/// seat three before the button is UTG; at a full-ring table it is the lojack, and UTG is six before.
/// A short-handed full-ring table (six dealt at a 9-max) keeps full-ring names: its first seat is a
/// lojack, as players there think of it.
/// </summary>
public static class OpeningSeat
{
    private static readonly PokerPosition[] FullRing =
    [
        PokerPosition.Button,
        PokerPosition.Cutoff,
        PokerPosition.Hijack,
        PokerPosition.Lojack,
        PokerPosition.Utg2,
        PokerPosition.Utg1,
        PokerPosition.Utg,
    ];

    private static readonly PokerPosition[] SixMax =
    [
        PokerPosition.Button,
        PokerPosition.Cutoff,
        PokerPosition.Hijack,
        PokerPosition.Utg,
    ];

    /// <param name="playersDealt">Players dealt in the hand, blinds included.</param>
    public static PokerPosition Canonical(PokerPosition position, int playersDealt, TableFormat format)
    {
        // PositionResolver names the first seats UTG, UTG+1, UTG+2 in that order; the others keep their name.
        var distance = position switch
        {
            PokerPosition.Utg => playersDealt - 3,
            PokerPosition.Utg1 => playersDealt - 4,
            PokerPosition.Utg2 => playersDealt - 5,
            PokerPosition.Lojack => 3,
            PokerPosition.Hijack => 2,
            PokerPosition.Cutoff => 1,
            PokerPosition.Button => 0,
            _ => -1,
        };
        if (distance < 0)
        {
            return position;
        }

        var seats = format == TableFormat.SixMax ? SixMax : FullRing;
        return seats[Math.Min(distance, seats.Length - 1)];
    }

    /// <summary>
    /// The full-ring seat at the same distance from the button: what reference ranges are written for
    /// (a 6-max UTG opens like a full-ring lojack).
    /// </summary>
    public static PokerPosition FullRingEquivalent(PokerPosition position, TableFormat format) =>
        format == TableFormat.SixMax && position == PokerPosition.Utg ? PokerPosition.Lojack : position;
}
