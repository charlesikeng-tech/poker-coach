using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Import;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Import;

/// <summary>
/// One uploaded file: its raw content (gzip, kept to re-import after parser fixes and to investigate
/// rejections) and its place in the processing queue. See ADR-0005.
/// </summary>
internal sealed class ImportedFileRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid BatchId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentSha256 { get; set; } = string.Empty;

    public byte[] ContentGzip { get; set; } = [];

    public long SizeBytes { get; set; }

    public string Status { get; set; } = ImportStatusText.Pending;

    public string? Kind { get; set; }

    public int Attempts { get; set; }

    /// <summary>Claim expiry while processing; retry-not-before while pending after an error.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public string? ErrorCode { get; set; }

    public Guid? PokerAccountId { get; set; }

    public int? HandsImported { get; set; }

    public int? HandsAlreadyPresent { get; set; }

    public int? HandsRejected { get; set; }

    public string? Rejections { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Text values stored in the status and kind columns (readable in SQL, checked by constraints).</summary>
internal static class ImportStatusText
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public const string HandHistory = "hand_history";
    public const string TournamentSummary = "tournament_summary";

    public static string From(ImportFileStatus status) => status switch
    {
        ImportFileStatus.Pending => Pending,
        ImportFileStatus.Processing => Processing,
        ImportFileStatus.Completed => Completed,
        ImportFileStatus.Failed => Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static ImportFileStatus ToStatus(string value) => value switch
    {
        Pending => ImportFileStatus.Pending,
        Processing => ImportFileStatus.Processing,
        Completed => ImportFileStatus.Completed,
        Failed => ImportFileStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string? From(ImportFileKind? kind) => kind switch
    {
        null => null,
        ImportFileKind.HandHistory => HandHistory,
        ImportFileKind.TournamentSummary => TournamentSummary,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ImportFileKind? ToKind(string? value) => value switch
    {
        null => null,
        HandHistory => ImportFileKind.HandHistory,
        TournamentSummary => ImportFileKind.TournamentSummary,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}

internal static class ImportSchema
{
    public const string Name = "import";
}

internal sealed class ImportedFileConfiguration : IEntityTypeConfiguration<ImportedFileRecord>
{
    public void Configure(EntityTypeBuilder<ImportedFileRecord> builder)
    {
        builder.ToTable("imported_files", ImportSchema.Name, table =>
        {
            table.HasCheckConstraint(
                "ck_imported_files_status",
                $"status IN ('{ImportStatusText.Pending}', '{ImportStatusText.Processing}', '{ImportStatusText.Completed}', '{ImportStatusText.Failed}')");
            table.HasCheckConstraint(
                "ck_imported_files_kind",
                $"kind IS NULL OR kind IN ('{ImportStatusText.HandHistory}', '{ImportStatusText.TournamentSummary}')");
        });
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.FileName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.ContentSha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(f => f.ContentGzip).IsRequired();
        builder.Property(f => f.Status).HasMaxLength(16).IsRequired();
        builder.Property(f => f.Kind).HasMaxLength(32);
        builder.Property(f => f.ErrorCode).HasMaxLength(64);
        builder.Property(f => f.Rejections).HasColumnType("jsonb");

        // File deduplication: the same content uploaded twice by a user is stored and processed once.
        builder.HasIndex(f => new { f.UserId, f.ContentSha256 }).IsUnique();
        builder.HasIndex(f => new { f.UserId, f.BatchId });

        // The queue: the worker scans only waiting files, oldest first.
        builder.HasIndex(f => f.CreatedAt)
            .HasFilter($"status IN ('{ImportStatusText.Pending}', '{ImportStatusText.Processing}')");

        builder.HasOne<User>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);

        // Deleting a poker account ("this is not me") removes its files, so they can be uploaded again.
        builder.HasOne<PokerAccountRecord>().WithMany().HasForeignKey(f => f.PokerAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
