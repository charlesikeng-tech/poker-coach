using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories;

/// <summary>
/// A provider tournament summary as printed. Nullable fields are null when the line is absent from the
/// file: that means UNKNOWN, never zero. Interpreting them (ROI, bounty winnings when the line is
/// missing) is the job of the import and performance modules, not of the parser.
/// </summary>
public sealed record ParsedTournamentSummary
{
    public required PokerRoom Room { get; init; }

    public required string ExternalTournamentId { get; init; }

    public required string TournamentName { get; init; }

    public required string PlayerName { get; init; }

    /// <summary>Part of one buy-in that goes to the prize pool.</summary>
    public required decimal PrizePoolBuyIn { get; init; }

    /// <summary>Part of one buy-in that funds the player's own bounty; null when the buy-in has no bounty part.</summary>
    public required decimal? BountyBuyIn { get; init; }

    public required decimal Fee { get; init; }

    public required string Currency { get; init; }

    /// <summary>Unique registered players. Re-entries are not counted here.</summary>
    public required int RegisteredPlayers { get; init; }

    /// <summary>Raw provider values (e.g. "tt", "knockout", "semiturbo"); mapped to domain formats downstream.</summary>
    public required string? Mode { get; init; }

    public required string? Type { get; init; }

    public required string? Speed { get; init; }

    public required string? FlightId { get; init; }

    public required decimal PrizePool { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// One per buy-in of the player, in the order played: a re-entry adds an entry. Never empty.
    /// Tournament-level values above (registered players, prize pool) come from the last entry's block:
    /// they are a snapshot taken when the player was last eliminated, not necessarily final.
    /// </summary>
    public required IReadOnlyList<ParsedTournamentEntry> Entries { get; init; }
}

/// <param name="EntryNumber">1-based, in file order (first buy-in first).</param>
/// <param name="LateRegistration">
/// Registered during late registration (header suffix " - Late Registration"): started with the starting
/// stack at a later level, i.e. fewer big blinds.
/// </param>
/// <param name="PlayedDuration">As printed for this entry; whether it is cumulative across entries is unknown.</param>
/// <param name="PrizeWinnings">Null when not printed (out of the money, or bounties only): unknown, not zero.</param>
/// <param name="BountyWinnings">Null when the summary does not print a bounty amount.</param>
public sealed record ParsedTournamentEntry(
    int EntryNumber,
    bool LateRegistration,
    TimeSpan? PlayedDuration,
    int? FinishPosition,
    decimal? PrizeWinnings,
    decimal? BountyWinnings);
