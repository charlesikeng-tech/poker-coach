using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Training;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Training;

internal static class TrainingSchema
{
    public const string Name = "training";
}

/// <summary>One answer in an open-or-fold drill. Kept with the reference version it was checked against.</summary>
internal sealed class OpeningAttemptRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public TableFormat Format { get; set; }

    public StackBand Band { get; set; }

    public PokerPosition Position { get; set; }

    /// <summary>Starting hand in text form: "AKs".</summary>
    public string Hand { get; set; } = string.Empty;

    /// <summary>Stack of a push/fold drill, whose answer depends on it; null for the other bands.</summary>
    public int? PushStack { get; set; }

    /// <summary>Defence drills: the seat that shoved; null for opening drills.</summary>
    public PokerPosition? Shover { get; set; }

    /// <summary>Quiz on real hands: the hand the spot comes from. Not a foreign key: deleting a hand (or
    /// re-importing it) must not erase the player's training record.</summary>
    public Guid? SourceHandId { get; set; }

    public DrillAnswer Answer { get; set; }

    public bool Correct { get; set; }

    public int ReferenceVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class OpeningAttemptConfiguration : IEntityTypeConfiguration<OpeningAttemptRecord>
{
    public void Configure(EntityTypeBuilder<OpeningAttemptRecord> builder)
    {
        builder.ToTable("opening_attempts", TrainingSchema.Name);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Format).HasConversion<int>();
        builder.Property(a => a.Band).HasConversion<int>();
        builder.Property(a => a.Position).HasConversion<int>();
        builder.Property(a => a.Shover).HasConversion<int?>();
        builder.Property(a => a.Answer).HasConversion<int>();
        builder.Property(a => a.Hand).HasMaxLength(3).IsRequired();
        // Reads are "this user's latest attempts at a format".
        builder.HasIndex(a => new { a.UserId, a.Format, a.CreatedAt });
        // The quiz asks "which real hands did this user already fix?".
        builder.HasIndex(a => new { a.UserId, a.SourceHandId }).HasFilter("source_hand_id IS NOT NULL");
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TrainingStore(PokerCoachDbContext db) : ITrainingStore
{
    public async Task RecordAsync(Guid userId, DrillAttempt attempt, int referenceVersion, CancellationToken cancellationToken)
    {
        db.Add(new OpeningAttemptRecord
        {
            Id = Guid.CreateVersion7(attempt.AnsweredAt),
            UserId = userId,
            Format = attempt.Item.Format,
            Band = attempt.Item.Band,
            Position = attempt.Item.Position,
            Hand = attempt.Item.Hand.ToString(),
            PushStack = attempt.Item.PushStack,
            Shover = attempt.Item.Shover,
            SourceHandId = attempt.SourceHandId,
            Answer = attempt.Answer,
            Correct = attempt.Correct,
            ReferenceVersion = referenceVersion,
            CreatedAt = attempt.AnsweredAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>Latest answer per real hand: a hand fixed once stays fixed, a miss brings it back.</summary>
    public async Task<IReadOnlyDictionary<Guid, bool>> RealHandOutcomesAsync(Guid userId, TableFormat format, CancellationToken cancellationToken)
    {
        var rows = await db.Set<OpeningAttemptRecord>().AsNoTracking()
            .Where(a => a.UserId == userId && a.Format == format && a.SourceHandId != null)
            .Select(a => new { HandId = a.SourceHandId!.Value, a.Correct, a.CreatedAt })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.HandId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First().Correct);
    }

    public async Task<(int Attempts, int Correct)> CountSinceAsync(Guid userId, DateTimeOffset since, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        var counts = await db.Set<OpeningAttemptRecord>().AsNoTracking()
            .Where(a => a.UserId == userId && a.CreatedAt >= since && (until == null || a.CreatedAt < until))
            .GroupBy(_ => 1)
            .Select(g => new { Attempts = g.Count(), Correct = g.Count(a => a.Correct) })
            .SingleOrDefaultAsync(cancellationToken);
        return counts is null ? (0, 0) : (counts.Attempts, counts.Correct);
    }

    /// <summary>Attempts checked against another reference version are left out: their answer key changed.</summary>
    public async Task<IReadOnlyList<DrillAttempt>> RecentAsync(Guid userId, TableFormat format, DrillMode mode, int count, CancellationToken cancellationToken)
    {
        var defence = mode == DrillMode.Defence;
        var rows = await db.Set<OpeningAttemptRecord>().AsNoTracking()
            .Where(a => a.UserId == userId && a.Format == format && a.ReferenceVersion == ReferenceOpeningRanges.Version)
            .Where(a => (a.Shover != null) == defence)
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .Select(a => new { a.Band, a.Position, a.Hand, a.PushStack, a.Shover, a.Answer, a.Correct, a.CreatedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => (Row: r, Hand: RangeNotation.Parse(r.Hand)))
            .Where(x => x.Hand.Count == 1)
            .Select(x => new DrillAttempt(new DrillItem(format, x.Row.Band, x.Row.Position, x.Hand.Single(), x.Row.PushStack, x.Row.Shover), x.Row.Answer, x.Row.Correct, x.Row.CreatedAt))
            .ToList();
    }
}
