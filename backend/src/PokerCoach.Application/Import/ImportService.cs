using System.Security.Cryptography;

namespace PokerCoach.Application.Import;

public sealed record UploadResult(Guid BatchId, IReadOnlyList<QueuedFile> Files, IReadOnlyList<RejectedUpload> Rejected, bool TooManyFiles)
{
    public bool HasQueuedFiles => Files.Any(f => !f.AlreadyImported);
}

/// <summary>
/// Upload side of the import (ADR-0005): validates and stores files, then returns at once. Parsing is
/// done by <see cref="ImportProcessor"/> in the background, so a large upload never holds an HTTP request.
/// </summary>
public sealed class ImportService(IImportStore store, ImportOptions options, TimeProvider time)
{
    public async Task<UploadResult> UploadAsync(Guid userId, IReadOnlyList<UploadSource> sources, CancellationToken cancellationToken)
    {
        var expanded = await UploadExpander.ExpandAsync(sources, options, cancellationToken);
        var batchId = Guid.CreateVersion7(time.GetUtcNow());
        if (expanded.TooManyFiles)
        {
            return new UploadResult(batchId, [], [], TooManyFiles: true);
        }

        var files = expanded.Files
            .Select(f => new FileToImport(f.FileName, f.Content, Convert.ToHexStringLower(SHA256.HashData(f.Content))))
            .ToList();

        var queued = files.Count == 0
            ? []
            : await store.EnqueueAsync(userId, batchId, files, cancellationToken);

        return new UploadResult(batchId, queued, expanded.Rejected, TooManyFiles: false);
    }

    public Task<IReadOnlyList<ImportedFileView>> GetBatchAsync(Guid userId, Guid batchId, CancellationToken cancellationToken) =>
        store.GetBatchAsync(userId, batchId, cancellationToken);
}
