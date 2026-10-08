using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories;

namespace PokerCoach.Application.Import;

/// <summary>
/// Persistence port of the import pipeline (implemented in Infrastructure). Idempotency is enforced by
/// the database, not by checks made here: every "create" is an insert-or-get on a unique key.
/// </summary>
public interface IImportStore
{
    /// <summary>
    /// Queues files for processing. A file whose content this user already uploaded is not stored twice:
    /// it is reported with <see cref="QueuedFile.AlreadyImported"/>, unless its earlier processing failed,
    /// in which case it is queued again in this batch.
    /// </summary>
    Task<IReadOnlyList<QueuedFile>> EnqueueAsync(Guid userId, Guid batchId, IReadOnlyList<FileToImport> files, CancellationToken cancellationToken);

    Task<IReadOnlyList<ImportedFileView>> GetBatchAsync(Guid userId, Guid batchId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the oldest queued file for processing, or null when none is due. Safe under concurrent workers:
    /// a file is claimed by one worker at a time, and a claim abandoned by a crashed worker expires.
    /// Files that already used <paramref name="maxAttempts"/> are marked failed instead of being claimed.
    /// </summary>
    Task<ClaimedFile?> ClaimNextAsync(int maxAttempts, CancellationToken cancellationToken);

    Task CompleteAsync(Guid fileId, FileOutcome outcome, CancellationToken cancellationToken);

    /// <summary>Gives a claimed file back after an unexpected error: queued again later, or failed after <paramref name="maxAttempts"/>.</summary>
    Task ReleaseAfterErrorAsync(Guid fileId, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="work"/> in one database transaction; the store's other methods join it.</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    Task<Guid> GetOrCreatePokerAccountAsync(Guid userId, PokerRoom room, string screenName, CancellationToken cancellationToken);

    /// <summary>Creates the tournament on its first hand; later hands only complete what is still unknown.</summary>
    Task<Guid> EnsureTournamentAsync(Guid pokerAccountId, string externalTournamentId, ParsedHand hand, CancellationToken cancellationToken);

    Task UpsertTournamentSummaryAsync(Guid pokerAccountId, ParsedTournamentSummary summary, CancellationToken cancellationToken);

    /// <returns>False when the account already has this hand (re-import, overlapping files).</returns>
    Task<bool> TryInsertHandAsync(Guid pokerAccountId, Guid tournamentId, Guid importedFileId, ParsedHand hand, CancellationToken cancellationToken);
}

/// <param name="ContentSha256">Lowercase hex SHA-256 of <paramref name="Content"/>: the file deduplication key.</param>
public sealed record FileToImport(string FileName, byte[] Content, string ContentSha256);

public sealed record QueuedFile(Guid FileId, string FileName, ImportFileStatus Status, bool AlreadyImported);

public sealed record ClaimedFile(Guid Id, Guid UserId, string FileName, byte[] Content, int Attempts);

public enum ImportFileStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}

public enum ImportFileKind
{
    HandHistory,
    TournamentSummary,
}

/// <param name="ErrorCode">One of <see cref="ImportErrorCodes"/> when <paramref name="Status"/> is failed.</param>
/// <param name="PokerAccountId">The account the file was imported into; null when the file failed before that.</param>
/// <param name="Rejections">Rejected hands or lines, capped (see <see cref="ImportProcessor"/>); <paramref name="HandsRejected"/> is the full count.</param>
public sealed record FileOutcome(
    ImportFileStatus Status,
    ImportFileKind? Kind,
    string? ErrorCode,
    Guid? PokerAccountId,
    int HandsImported,
    int HandsAlreadyPresent,
    int HandsRejected,
    IReadOnlyList<ParseError> Rejections);

public sealed record ImportedFileView(
    Guid FileId,
    string FileName,
    ImportFileStatus Status,
    ImportFileKind? Kind,
    string? ErrorCode,
    string? ScreenName,
    int? HandsImported,
    int? HandsAlreadyPresent,
    int? HandsRejected,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
