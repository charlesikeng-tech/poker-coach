namespace PokerCoach.HandHistories;

/// <summary>
/// Why a file or a hand was rejected. <see cref="Code"/> is one of <see cref="ParseErrorCodes"/>.
/// Errors never carry the offending line's content: it holds player pseudonyms (privacy) and the
/// line number is enough to investigate with the stored raw file.
/// </summary>
/// <param name="LineNumber">1-based line in the file.</param>
/// <param name="HandId">Provider hand id, when the hand header could be read.</param>
public sealed record ParseError(string Code, int LineNumber, string? HandId);

/// <summary>Machine-readable parse error codes. Stable contract: add freely, never rename.</summary>
public static class ParseErrorCodes
{
    public const string EmptyFile = "EMPTY_FILE";
    public const string UnrecognizedFormat = "UNRECOGNIZED_FORMAT";
    public const string UnrecognizedLine = "UNRECOGNIZED_LINE";
    public const string LineTooLong = "LINE_TOO_LONG";
    public const string UnexpectedSection = "UNEXPECTED_SECTION";
    public const string MissingSeats = "MISSING_SEATS";
    public const string DuplicatePlayer = "DUPLICATE_PLAYER";
    public const string UnknownPlayer = "UNKNOWN_PLAYER";
    public const string InvalidNumber = "INVALID_NUMBER";
    public const string InvalidCard = "INVALID_CARD";
    public const string InvalidBoard = "INVALID_BOARD";
    public const string InvalidHoleCards = "INVALID_HOLE_CARDS";
    public const string InconsistentRaise = "INCONSISTENT_RAISE";
    public const string UnsupportedBuyIn = "UNSUPPORTED_BUY_IN";
    public const string IncompleteHand = "INCOMPLETE_HAND";
    public const string PotMismatch = "POT_MISMATCH";
    public const string BoardMismatch = "BOARD_MISMATCH";
    public const string MissingField = "MISSING_FIELD";
}
