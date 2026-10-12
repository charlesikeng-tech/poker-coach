using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Training;

/// <summary>
/// One answer in a sizing drill (ADR-0020). Its own table rather than columns on the opening attempts: the answer
/// is a size, not a fold/raise/call, and the spot carries an opener and an open size.
/// </summary>
internal sealed class SizingAttemptRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public TableFormat Format { get; set; }

    public StackBand Band { get; set; }

    public PokerPosition Position { get; set; }

    public decimal StackBigBlinds { get; set; }

    /// <summary>3-bet drills: the opener and the open faced (raise-to in big blinds); null for open drills.</summary>
    public PokerPosition? Opener { get; set; }

    public decimal? OpenToBigBlinds { get; set; }

    /// <summary>The size answered (big blinds for an open, a multiple for a 3-bet); null: all-in.</summary>
    public decimal? AnswerSize { get; set; }

    public bool Correct { get; set; }

    public int ReferenceVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the attempt is an answer to a real badly-sized hand (not a reference drill spot).</summary>
    public Guid? SourceHandId { get; set; }
}

internal sealed class SizingAttemptConfiguration : IEntityTypeConfiguration<SizingAttemptRecord>
{
    public void Configure(EntityTypeBuilder<SizingAttemptRecord> builder)
    {
        builder.ToTable("sizing_attempts", TrainingSchema.Name);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Format).HasConversion<int>();
        builder.Property(a => a.Band).HasConversion<int>();
        builder.Property(a => a.Position).HasConversion<int>();
        builder.Property(a => a.Opener).HasConversion<int?>();
        builder.Property(a => a.StackBigBlinds).HasPrecision(6, 2);
        builder.Property(a => a.OpenToBigBlinds).HasPrecision(5, 2);
        builder.Property(a => a.AnswerSize).HasPrecision(5, 2);
        // Reads are "this user's latest attempts at a format".
        builder.HasIndex(a => new { a.UserId, a.Format, a.CreatedAt });
        // Reads for real-spot outcomes: which real hands did this user already answer correctly?
        builder.HasIndex(a => new { a.UserId, a.Format, a.SourceHandId })
            .HasFilter("source_hand_id IS NOT NULL");
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SizingTrainingStore(PokerCoachDbContext db) : ISizingTrainingStore
{
    public async Task RecordAsync(Guid userId, SizingAttempt attempt, int referenceVersion, CancellationToken cancellationToken)
    {
        db.Add(new SizingAttemptRecord
        {
            Id = Guid.CreateVersion7(attempt.AnsweredAt),
            UserId = userId,
            Format = attempt.Item.Format,
            Band = attempt.Item.Band,
            Position = attempt.Item.Position,
            StackBigBlinds = attempt.Item.StackBigBlinds,
            Opener = attempt.Item.Opener,
            OpenToBigBlinds = attempt.Item.OpenToBigBlinds,
            AnswerSize = attempt.Answer.Value,
            Correct = attempt.Correct,
            ReferenceVersion = referenceVersion,
            CreatedAt = attempt.AnsweredAt,
            SourceHandId = attempt.SourceHandId,
        });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlySet<Guid>> RealHandOutcomesAsync(Guid userId, TableFormat format, CancellationToken cancellationToken)
    {
        var ids = await db.Set<SizingAttemptRecord>().AsNoTracking()
            .Where(a => a.UserId == userId && a.Format == format && a.SourceHandId != null && a.Correct)
            .Select(a => a.SourceHandId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        return new HashSet<Guid>(ids);
    }

    /// <summary>Attempts checked against another reference version are left out: their answer key changed.</summary>
    public async Task<IReadOnlyList<SizingAttempt>> RecentAsync(Guid userId, TableFormat format, int count, CancellationToken cancellationToken)
    {
        var rows = await db.Set<SizingAttemptRecord>().AsNoTracking()
            .Where(a => a.UserId == userId && a.Format == format && a.ReferenceVersion == ReferenceSizings.Version)
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .Select(a => new { a.Band, a.Position, a.StackBigBlinds, a.Opener, a.OpenToBigBlinds, a.AnswerSize, a.Correct, a.CreatedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new SizingAttempt(
                new SizingItem(format, r.Band, r.Position, r.StackBigBlinds, r.Opener, r.OpenToBigBlinds),
                new RaiseSize(r.AnswerSize),
                r.Correct,
                r.CreatedAt))
            .ToList();
    }
}
