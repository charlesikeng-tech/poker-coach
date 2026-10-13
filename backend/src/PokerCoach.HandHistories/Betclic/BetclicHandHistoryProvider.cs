using System.Diagnostics.CodeAnalysis;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Betclic;

/// <summary>
/// Betclic hand history provider — parsers not yet implemented.
/// Detect always returns Unknown so the import pipeline skips Betclic files gracefully.
/// </summary>
public sealed class BetclicHandHistoryProvider : IHandHistoryProvider
{
    public PokerRoom Room => PokerRoom.Betclic;

    public IHandHistoryParser HandHistories { get; } = new BetclicHandHistoryParser();

    public ITournamentSummaryParser TournamentSummaries { get; } = new BetclicTournamentSummaryParser();

    public HandHistoryFileKind Detect(string content) => HandHistoryFileKind.Unknown;

    public bool TryReadTableSequence(string externalHandId, [NotNullWhen(true)] out TableSequence? sequence)
    {
        sequence = null;
        return false;
    }
}

internal sealed class BetclicHandHistoryParser : IHandHistoryParser
{
    public PokerRoom Room => PokerRoom.Betclic;

    public HandHistoryParseResult Parse(string content) =>
        new([], [new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null)]);
}

internal sealed class BetclicTournamentSummaryParser : ITournamentSummaryParser
{
    public PokerRoom Room => PokerRoom.Betclic;

    public TournamentSummaryParseResult Parse(string content) =>
        new(null, [new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null)]);
}
