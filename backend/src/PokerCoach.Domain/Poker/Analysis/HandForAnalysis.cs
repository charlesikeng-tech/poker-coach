namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>Provider-neutral action kinds the analysis needs. Values are persisted nowhere.</summary>
public enum ActionKind
{
    PostAnte,
    PostSmallBlind,
    PostBigBlind,
    Fold,
    Check,
    Call,
    Bet,
    Raise,
}

public sealed record SeatState(int SeatNumber, string Player, long Stack);

/// <param name="Amount">Chips this action adds to the pot (a raise's increment, not its total).</param>
public sealed record HandAction(Street Street, string Player, ActionKind Kind, long Amount, bool IsAllIn);

/// <summary>
/// The facts of one hand the analysis reads, from the hero's point of view. Built from a parsed hand
/// at import, or from the stored hand when facts are recomputed.
/// </summary>
/// <param name="Seats">Everyone seated; who was dealt in is derived from <paramref name="Actions"/>.</param>
/// <param name="Collected">Chips collected per player (uncalled bets included, as printed by the room).</param>
public sealed record HandForAnalysis(
    int ButtonSeat,
    long BigBlind,
    string Hero,
    IReadOnlyList<SeatState> Seats,
    IReadOnlyList<HandAction> Actions,
    IReadOnlyDictionary<string, long> Collected);
