using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>
/// Opening ranges depend on how many players are left to act, not on the seat's name: "UTG" at a 6-handed
/// table is the lojack of a full ring. Positions before the hijack are therefore renamed by their distance
/// to the button (0 button, 1 cutoff, 2 hijack, 3 lojack, 4 UTG+2, 5 UTG+1, 6 UTG), so ranges compare
/// across table sizes.
/// </summary>
public static class OpeningSeat
{
    private static readonly PokerPosition[] ByDistance =
    [
        PokerPosition.Button,
        PokerPosition.Cutoff,
        PokerPosition.Hijack,
        PokerPosition.Lojack,
        PokerPosition.Utg2,
        PokerPosition.Utg1,
        PokerPosition.Utg,
    ];

    /// <param name="playersDealt">Players dealt in the hand, blinds included.</param>
    public static PokerPosition Canonical(PokerPosition position, int playersDealt)
    {
        // PositionResolver names the first seats UTG, UTG+1, UTG+2 in that order; the others keep their name.
        var distance = position switch
        {
            PokerPosition.Utg => playersDealt - 3,
            PokerPosition.Utg1 => playersDealt - 4,
            PokerPosition.Utg2 => playersDealt - 5,
            _ => -1,
        };

        return distance < 0 ? position : ByDistance[Math.Min(distance, ByDistance.Length - 1)];
    }
}
