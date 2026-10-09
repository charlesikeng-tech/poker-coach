namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>Seat relative to the button, named the usual way. Values are persisted: never renumber.</summary>
public enum PokerPosition
{
    Utg = 1,
    Utg1 = 2,
    Utg2 = 3,
    Lojack = 4,
    Hijack = 5,
    Cutoff = 6,
    Button = 7,
    SmallBlind = 8,
    BigBlind = 9,
}

/// <summary>
/// Positions of the players dealt in. Blinds are whoever posted them (a dead small blind leaves no small
/// blind); the button is the button seat when that player was dealt in. Players between the big blind and
/// the button are named from the button backwards (cutoff, hijack, lojack, UTG+2, UTG+1), the first one
/// after the big blind always being UTG. Heads-up, the button posts the small blind and is the button.
/// </summary>
public static class PositionResolver
{
    // Names of the players between the big blind and the button, by how many there are (index = count).
    private static readonly PokerPosition[][] Middle =
    [
        [],
        [PokerPosition.Utg],
        [PokerPosition.Utg, PokerPosition.Cutoff],
        [PokerPosition.Utg, PokerPosition.Hijack, PokerPosition.Cutoff],
        [PokerPosition.Utg, PokerPosition.Lojack, PokerPosition.Hijack, PokerPosition.Cutoff],
        [PokerPosition.Utg, PokerPosition.Utg1, PokerPosition.Lojack, PokerPosition.Hijack, PokerPosition.Cutoff],
        [PokerPosition.Utg, PokerPosition.Utg1, PokerPosition.Utg2, PokerPosition.Lojack, PokerPosition.Hijack, PokerPosition.Cutoff],
    ];

    public static IReadOnlyDictionary<string, PokerPosition> Resolve(HandForAnalysis hand)
    {
        ArgumentNullException.ThrowIfNull(hand);

        var dealt = hand.Actions.Select(a => a.Player).ToHashSet(StringComparer.Ordinal);
        var seats = hand.Seats
            .Where(s => dealt.Contains(s.Player))
            // Clockwise from the seat after the button; the button itself comes last.
            .OrderBy(s => s.SeatNumber > hand.ButtonSeat ? s.SeatNumber : s.SeatNumber + 1_000)
            .ToList();

        var positions = new Dictionary<string, PokerPosition>(StringComparer.Ordinal);
        var smallBlind = hand.Actions.FirstOrDefault(a => a.Kind == ActionKind.PostSmallBlind)?.Player;
        var bigBlind = hand.Actions.FirstOrDefault(a => a.Kind == ActionKind.PostBigBlind)?.Player;
        var button = seats.FirstOrDefault(s => s.SeatNumber == hand.ButtonSeat)?.Player;

        if (seats.Count == 2)
        {
            foreach (var seat in seats)
            {
                positions[seat.Player] = seat.Player == bigBlind ? PokerPosition.BigBlind : PokerPosition.Button;
            }

            return positions;
        }

        if (button is not null)
        {
            positions[button] = PokerPosition.Button;
        }

        if (smallBlind is not null && smallBlind != button)
        {
            positions[smallBlind] = PokerPosition.SmallBlind;
        }

        if (bigBlind is not null)
        {
            positions[bigBlind] = PokerPosition.BigBlind;
        }

        var others = seats.Where(s => !positions.ContainsKey(s.Player)).ToList();
        if (others.Count < Middle.Length)
        {
            for (var i = 0; i < others.Count; i++)
            {
                positions[others[i].Player] = Middle[others.Count][i];
            }
        }

        return positions;
    }
}
