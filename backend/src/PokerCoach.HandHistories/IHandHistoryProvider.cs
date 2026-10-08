using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories;

/// <summary>
/// Everything the import needs from one poker room: recognise its files, then parse them.
/// One implementation per room; the import tries each provider until one recognises the file.
/// </summary>
public interface IHandHistoryProvider
{
    PokerRoom Room { get; }

    IHandHistoryParser HandHistories { get; }

    ITournamentSummaryParser TournamentSummaries { get; }

    /// <summary>Kind of file from its content, never from its name. <see cref="HandHistoryFileKind.Unknown"/> if not this room's.</summary>
    HandHistoryFileKind Detect(string content);
}
