using System.IO.Compression;

namespace PokerCoach.Application.Import;

/// <param name="Open">Opens the uploaded content; the caller owns the source until expansion ends.</param>
public sealed record UploadSource(string FileName, Func<Stream> Open);

public sealed record IncomingFile(string FileName, byte[] Content);

public sealed record RejectedUpload(string FileName, string Code);

public sealed record ExpandedUpload(IReadOnlyList<IncomingFile> Files, IReadOnlyList<RejectedUpload> Rejected, bool TooManyFiles);

/// <summary>
/// Turns an upload (text files and zip archives) into candidate hand-history files, treating every byte
/// as hostile: names are reduced to a bare file name (no path is ever used), sizes are enforced on the
/// bytes actually read (an archive cannot lie about its decompressed size), nested archives are refused.
/// </summary>
public static class UploadExpander
{
    private const int ReadChunkBytes = 81_920;
    private const int MaxFileNameLength = 255;

    public static async Task<ExpandedUpload> ExpandAsync(
        IReadOnlyList<UploadSource> sources,
        ImportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);

        var files = new List<IncomingFile>();
        var rejected = new List<RejectedUpload>();
        long expandedBytes = 0;

        foreach (var source in sources)
        {
            var name = SafeFileName(source.FileName);
            if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = source.Open();
                AddTextFile(name, await ReadBoundedAsync(stream, options.MaxFileBytes, cancellationToken), files, rejected);
            }
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                expandedBytes = await ExpandArchiveAsync(source, name, options, expandedBytes, files, rejected, cancellationToken);
            }
            else
            {
                rejected.Add(new RejectedUpload(name, ImportErrorCodes.UnsupportedFileType));
            }

            if (files.Count > options.MaxFilesPerUpload)
            {
                return new ExpandedUpload([], [], TooManyFiles: true);
            }
        }

        return new ExpandedUpload(files, rejected, TooManyFiles: false);
    }

    private static async Task<long> ExpandArchiveAsync(
        UploadSource source,
        string archiveName,
        ImportOptions options,
        long expandedBytes,
        List<IncomingFile> files,
        List<RejectedUpload> rejected,
        CancellationToken cancellationToken)
    {
        await using var stream = source.Open();
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            rejected.Add(new RejectedUpload(archiveName, ImportErrorCodes.InvalidArchive));
            return expandedBytes;
        }

        using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                var name = SafeFileName(entry.FullName);

                // Directories and macOS resource forks are archive noise, not files.
                if (entry.FullName.EndsWith('/') || entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal)
                    || name.StartsWith("._", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    rejected.Add(new RejectedUpload(name, ImportErrorCodes.UnsupportedFileType));
                    continue;
                }

                if (expandedBytes >= options.MaxExpandedBytes)
                {
                    rejected.Add(new RejectedUpload(name, ImportErrorCodes.UploadTooLarge));
                    continue;
                }

                byte[]? content;
                var remainingForUpload = options.MaxExpandedBytes - expandedBytes;
                try
                {
                    await using var entryStream = entry.Open();
                    content = await ReadBoundedAsync(entryStream, Math.Min(options.MaxFileBytes, remainingForUpload), cancellationToken);
                }
                catch (InvalidDataException)
                {
                    rejected.Add(new RejectedUpload(name, ImportErrorCodes.InvalidArchive));
                    continue;
                }

                if (content is null && remainingForUpload < options.MaxFileBytes)
                {
                    // The file may be fine on its own: it is the upload as a whole that is too large.
                    rejected.Add(new RejectedUpload(name, ImportErrorCodes.UploadTooLarge));
                    expandedBytes = options.MaxExpandedBytes;
                    continue;
                }

                expandedBytes += content?.Length ?? 0;
                AddTextFile(name, content, files, rejected);
                if (files.Count > options.MaxFilesPerUpload)
                {
                    break;
                }
            }
        }

        return expandedBytes;
    }

    private static void AddTextFile(string name, byte[]? content, List<IncomingFile> files, List<RejectedUpload> rejected)
    {
        if (content is null)
        {
            rejected.Add(new RejectedUpload(name, ImportErrorCodes.FileTooLarge));
        }
        else if (content.Length == 0)
        {
            rejected.Add(new RejectedUpload(name, ImportErrorCodes.EmptyFile));
        }
        else
        {
            files.Add(new IncomingFile(name, content));
        }
    }

    /// <returns>The content, or null when it exceeds <paramref name="maxBytes"/>.</returns>
    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Bare file name: client paths and archive paths are never trusted nor stored.</summary>
    internal static string SafeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (name.Length > MaxFileNameLength)
        {
            name = name[..MaxFileNameLength];
        }

        return name.Length == 0 ? "file" : name;
    }
}
