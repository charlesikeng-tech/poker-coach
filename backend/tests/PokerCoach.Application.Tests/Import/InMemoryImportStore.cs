using PokerCoach.Application.Import;
using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories;

namespace PokerCoach.Application.Tests.Import;

/// <summary>
/// Mimics the store's contract (insert-or-get on natural keys) without a database. The SQL itself,
/// concurrency and transactions are covered by the PostgreSQL integration tests.
/// </summary>
internal sealed class InMemoryImportStore : IImportStore
{
    private readonly Queue<ClaimedFile> queue = new();

    public Dictionary<Guid, FileOutcome> Outcomes { get; } = [];

    public List<Guid> Released { get; } = [];

    public Dictionary<(Guid UserId, PokerRoom Room, string ScreenName), Guid> Accounts { get; } = [];

    public Dictionary<(Guid AccountId, string ExternalId), Guid> Tournaments { get; } = [];

    public Dictionary<(Guid AccountId, string ExternalId), ParsedTournamentSummary> Summaries { get; } = [];

    public HashSet<(Guid AccountId, string ExternalHandId)> Hands { get; } = [];

    public Exception? FailOnHandInsert { get; set; }

    public Guid Enqueue(Guid userId, byte[] content)
    {
        var id = Guid.NewGuid();
        queue.Enqueue(new ClaimedFile(id, userId, "file.txt", content, Attempts: 1));
        return id;
    }

    public Task<IReadOnlyList<QueuedFile>> EnqueueAsync(Guid userId, Guid batchId, IReadOnlyList<FileToImport> files, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ImportedFileView>> GetBatchAsync(Guid userId, Guid batchId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ClaimedFile?> ClaimNextAsync(int maxAttempts, CancellationToken cancellationToken) =>
        Task.FromResult(queue.TryDequeue(out var file) ? file : null);

    public Task CompleteAsync(Guid fileId, FileOutcome outcome, CancellationToken cancellationToken)
    {
        Outcomes[fileId] = outcome;
        return Task.CompletedTask;
    }

    public Task ReleaseAfterErrorAsync(Guid fileId, int maxAttempts, CancellationToken cancellationToken)
    {
        Released.Add(fileId);
        return Task.CompletedTask;
    }

    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) => work(cancellationToken);

    public Task<Guid> GetOrCreatePokerAccountAsync(Guid userId, PokerRoom room, string screenName, CancellationToken cancellationToken)
    {
        var key = (userId, room, screenName);
        if (!Accounts.TryGetValue(key, out var id))
        {
            Accounts[key] = id = Guid.NewGuid();
        }

        return Task.FromResult(id);
    }

    public Task<Guid> EnsureTournamentAsync(Guid pokerAccountId, string externalTournamentId, ParsedHand hand, CancellationToken cancellationToken) =>
        Task.FromResult(TournamentId(pokerAccountId, externalTournamentId));

    public Task UpsertTournamentSummaryAsync(Guid pokerAccountId, ParsedTournamentSummary summary, CancellationToken cancellationToken)
    {
        TournamentId(pokerAccountId, summary.ExternalTournamentId);
        Summaries[(pokerAccountId, summary.ExternalTournamentId)] = summary;
        return Task.CompletedTask;
    }

    public Task<bool> TryInsertHandAsync(Guid pokerAccountId, Guid tournamentId, Guid importedFileId, ParsedHand hand, CancellationToken cancellationToken) =>
        FailOnHandInsert is { } exception
            ? Task.FromException<bool>(exception)
            : Task.FromResult(Hands.Add((pokerAccountId, hand.ExternalHandId)));

    private Guid TournamentId(Guid accountId, string externalId)
    {
        if (!Tournaments.TryGetValue((accountId, externalId), out var id))
        {
            Tournaments[(accountId, externalId)] = id = Guid.NewGuid();
        }

        return id;
    }
}
