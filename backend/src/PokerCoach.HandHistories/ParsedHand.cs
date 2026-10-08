using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories;

/// <summary>
/// A hand exactly as the provider recorded it, in provider-neutral form. No poker interpretation is
/// added here (positions, effective stacks, uncalled bets are computed downstream from these facts).
/// Chip amounts are in tournament chips.
/// </summary>
public sealed record ParsedHand
{
    public required PokerRoom Room { get; init; }

    /// <summary>Provider hand id; unique per provider and the deduplication key.</summary>
    public required string ExternalHandId { get; init; }

    public required string TournamentName { get; init; }

    /// <summary>Null when the provider does not print it in the hand.</summary>
    public required string? ExternalTournamentId { get; init; }

    public required HandBuyIn BuyIn { get; init; }

    public required string TableName { get; init; }

    public required int MaxSeats { get; init; }

    public required bool IsRealMoney { get; init; }

    public required int Level { get; init; }

    /// <summary>Null when the provider does not print an ante for the level.</summary>
    public required long? Ante { get; init; }

    public required long SmallBlind { get; init; }

    public required long BigBlind { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required int ButtonSeat { get; init; }

    /// <summary>
    /// Everyone seated at the table. A seated player may take no part in the hand (just moved to the
    /// table): who was dealt in is derived from <see cref="Actions"/>, never from this list.
    /// </summary>
    public required IReadOnlyList<ParsedSeat> Seats { get; init; }

    /// <summary>The account owner, identified by the "dealt to" line. Null if absent.</summary>
    public required string? HeroName { get; init; }

    /// <summary>Empty when <see cref="HeroName"/> is null.</summary>
    public required IReadOnlyList<Card> HeroCards { get; init; }

    /// <summary>Forced bets and voluntary actions, in file order.</summary>
    public required IReadOnlyList<ParsedAction> Actions { get; init; }

    public required IReadOnlyList<Card> Board { get; init; }

    public required IReadOnlyList<ParsedShownCards> ShownCards { get; init; }

    /// <summary>
    /// Amounts collected. They include any uncalled bet returned to its owner: the provider does not
    /// print returns separately.
    /// </summary>
    public required IReadOnlyList<ParsedPotCollection> Collections { get; init; }

    /// <summary>Equals the sum of action amounts and the sum of collections (checked by the parser).</summary>
    public required long TotalPot { get; init; }

    /// <summary>1-based line of the hand header in the source file.</summary>
    public required int LineNumber { get; init; }
}

/// <summary>Buy-in as printed on hands: everything except the fee (prize pool and bounty parts together), and the fee.</summary>
public sealed record HandBuyIn(decimal AmountExcludingFee, decimal Fee, string Currency);

/// <param name="Bounty">Bounty on the player's head at the start of the hand, null in non-bounty formats.</param>
public sealed record ParsedSeat(int SeatNumber, string PlayerName, long Stack, decimal? Bounty);

/// <param name="Sequence">1-based order of the action within the hand, forced bets included.</param>
/// <param name="Amount">Chips this action adds to the pot (a raise's increment, not its total). Zero for folds and checks.</param>
/// <param name="RaiseTo">The player's total bet on the street after a raise; null for other actions.</param>
public sealed record ParsedAction(
    int Sequence,
    Street Street,
    string PlayerName,
    ParsedActionType Type,
    long Amount,
    long? RaiseTo,
    bool IsAllIn);

public enum ParsedActionType
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

/// <param name="HandDescription">Provider's description, kept for display only (localized, not parsed).</param>
public sealed record ParsedShownCards(string PlayerName, IReadOnlyList<Card> Cards, string? HandDescription);

/// <param name="SidePotNumber">Set when <paramref name="Kind"/> is <see cref="PotKind.Side"/>.</param>
public sealed record ParsedPotCollection(string PlayerName, long Amount, PotKind Kind, int? SidePotNumber);

public enum PotKind
{
    /// <summary>The whole pot (no side pot in the hand).</summary>
    Single,
    Main,
    Side,
}
