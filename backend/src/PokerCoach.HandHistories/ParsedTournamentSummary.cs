using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories;

/// <summary>
/// A provider tournament summary as printed. Nullable fields are null when the line is absent from the
/// file: that means UNKNOWN, never zero. Interpreting them (ROI, re-entries, bounty winnings when the
/// line is missing) is the job of the import and performance modules, not of the parser.
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

    public required TimeSpan? PlayedDuration { get; init; }

    public required int? FinishPosition { get; init; }

    public required decimal? PrizeWinnings { get; init; }

    /// <summary>Bounty cash won. Null when the summary does not print a bounty amount.</summary>
    public required decimal? BountyWinnings { get; init; }
}
