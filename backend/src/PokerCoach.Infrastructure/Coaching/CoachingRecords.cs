using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Infrastructure.Coaching;

internal static class CoachingSchema
{
    public const string Name = "coaching";
}

/// <summary>A generated explanation, reused while its fingerprint (leak, rate, references, language) holds.</summary>
internal sealed class LeakExplanationRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    /// <summary>The explanation and the example hand labels, as JSON.</summary>
    public string Payload { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A generated report (debrief, weekly review): the latest one per user, kind, subject and language.</summary>
internal sealed class CoachingReportRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = string.Empty;

    /// <summary>What it is about: a tournament id, a week's Monday (ISO date).</summary>
    public string Subject { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One billed model call: the ledger behind the budget cap and the per-user limit.</summary>
internal sealed class ModelUsageRecord
{
    public Guid Id { get; set; }

    /// <summary>Null once the user is deleted: spend must still count in the month's budget.</summary>
    public Guid? UserId { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int CacheWriteTokens { get; set; }

    public int CacheReadTokens { get; set; }

    public decimal CostUsd { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class LeakExplanationConfiguration : IEntityTypeConfiguration<LeakExplanationRecord>
{
    public void Configure(EntityTypeBuilder<LeakExplanationRecord> builder)
    {
        builder.ToTable("leak_explanations", CoachingSchema.Name);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Fingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(e => e.Language).HasMaxLength(8).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Model).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => new { e.UserId, e.Fingerprint }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CoachingReportConfiguration : IEntityTypeConfiguration<CoachingReportRecord>
{
    public void Configure(EntityTypeBuilder<CoachingReportRecord> builder)
    {
        builder.ToTable("reports", CoachingSchema.Name);
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Kind).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Subject).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Language).HasMaxLength(8).IsRequired();
        builder.Property(r => r.Fingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(r => r.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.Model).HasMaxLength(64).IsRequired();

        // One report per subject and language: generating again replaces it (upsert on this key).
        builder.HasIndex(r => new { r.UserId, r.Kind, r.Subject, r.Language }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ModelUsageConfiguration : IEntityTypeConfiguration<ModelUsageRecord>
{
    public void Configure(EntityTypeBuilder<ModelUsageRecord> builder)
    {
        builder.ToTable("model_usage", CoachingSchema.Name);
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(u => u.Model).HasMaxLength(64).IsRequired();
        builder.Property(u => u.CostUsd).HasPrecision(12, 6);
        builder.HasIndex(u => u.CreatedAt);
        builder.HasIndex(u => new { u.UserId, u.Purpose, u.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
