using System.Diagnostics.CodeAnalysis;
using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.PokerStars;

/// <summary>
/// PokerStars hand history provider — parsers not yet implemented.
/// Detect always returns Unknown so the import pipeline skips PokerStars files gracefully.
/// </summary>
public sealed class PokerStarsHandHistoryProvider : IHandHistoryProvider
{
    public PokerRoom Room => PokerRoom.PokerStars;

    public IHandHistoryParser HandHistories { get; } = new PokerStarsHandHistoryParser();

    public ITournamentSummaryParser TournamentSummaries { get; } = new PokerStarsTournamentSummaryParser();

    public HandHistoryFileKind Detect(string content) => HandHistoryFileKind.Unknown;

    public bool TryReadTableSequence(string externalHandId, [NotNullWhen(true)] out TableSequence? sequence)
    {
        sequence = null;
        return false;
    }
}

internal sealed class PokerStarsHandHistoryParser : IHandHistoryParser
{
    public PokerRoom Room => PokerRoom.PokerStars;

    public HandHistoryParseResult Parse(string content) =>
        new([], [new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null)]);
}

internal sealed class PokerStarsTournamentSummaryParser : ITournamentSummaryParser
{
    public PokerRoom Room => PokerRoom.PokerStars;

    public TournamentSummaryParseResult Parse(string content) =>
        new(null, [new ParseError(ParseErrorCodes.UnrecognizedFormat, 1, null)]);
}
