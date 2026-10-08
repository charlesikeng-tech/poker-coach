namespace PokerCoach.Application.Import;

/// <summary>Limits applied to untrusted uploads. Configuration section "Import".</summary>
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>One hand-history file (after decompression for zip entries).</summary>
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>Files per upload, zip entries included.</summary>
    public int MaxFilesPerUpload { get; set; } = 500;

    /// <summary>HTTP request body for one upload.</summary>
    public long MaxUploadBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>Total decompressed size of the archives of one upload (zip-bomb guard).</summary>
    public long MaxExpandedBytes { get; set; } = 500 * 1024 * 1024;

    /// <summary>A file failing this many times in a row (crash, timeout) is marked failed.</summary>
    public int MaxProcessingAttempts { get; set; } = 3;

    /// <summary>Background processing of queued files. Disabled in tests that do not need it.</summary>
    public bool WorkerEnabled { get; set; } = true;
}
