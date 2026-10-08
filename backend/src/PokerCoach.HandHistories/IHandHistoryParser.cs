using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories;

/// <summary>
/// Turns one provider hand-history file into normalized hands. Implementations are pure (no I/O) and
/// never throw on malformed input: every rejected hand is reported in
/// <see cref="HandHistoryParseResult.Errors"/>, and accepted hands are complete and self-consistent.
/// </summary>
public interface IHandHistoryParser
{
    PokerRoom Room { get; }

    HandHistoryParseResult Parse(string content);
}

public sealed record HandHistoryParseResult(IReadOnlyList<ParsedHand> Hands, IReadOnlyList<ParseError> Errors);

/// <summary>Reads a provider's end-of-tournament summary file.</summary>
public interface ITournamentSummaryParser
{
    PokerRoom Room { get; }

    TournamentSummaryParseResult Parse(string content);
}

/// <summary><see cref="Summary"/> is null when <see cref="Errors"/> is not empty: a summary is all or nothing.</summary>
public sealed record TournamentSummaryParseResult(ParsedTournamentSummary? Summary, IReadOnlyList<ParseError> Errors);
