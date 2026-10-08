namespace PokerCoach.Application.Import;

/// <summary>
/// File-level outcomes of an import, machine-readable. Hand-level rejections reuse the parser codes
/// (<see cref="HandHistories.ParseErrorCodes"/>). Stable contract: add freely, never rename.
/// </summary>
public static class ImportErrorCodes
{
    public const string UnsupportedFileType = "UNSUPPORTED_FILE_TYPE";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string EmptyFile = "EMPTY_FILE";
    public const string InvalidArchive = "INVALID_ARCHIVE";
    public const string UploadTooLarge = "UPLOAD_TOO_LARGE";
    public const string TooManyFiles = "TOO_MANY_FILES";
    public const string InvalidEncoding = "INVALID_ENCODING";
    public const string UnrecognizedFormat = "UNRECOGNIZED_FORMAT";
    public const string InvalidSummary = "INVALID_SUMMARY";
    public const string NoValidHand = "NO_VALID_HAND";
    public const string HeroNotFound = "HERO_NOT_FOUND";
    public const string TournamentUnknown = "TOURNAMENT_UNKNOWN";
    public const string ProcessingFailed = "PROCESSING_FAILED";
}
