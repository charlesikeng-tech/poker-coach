using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// Where a hand sits in its table's sequence, from the room's hand id. False when the room's ids do not
    /// carry it: hand-number gaps then cannot be detected (stack continuity still can).
    /// </summary>
    bool TryReadTableSequence(string externalHandId, [NotNullWhen(true)] out TableSequence? sequence);
}

/// <param name="TableKey">Identifies one table instance within the tournament.</param>
/// <param name="HandNumber">Consecutive hand number on that table.</param>
public sealed record TableSequence(string TableKey, long HandNumber);
