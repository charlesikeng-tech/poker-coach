using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Bankroll;
using PokerCoach.Domain.Bankroll;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Bankroll;

internal static class BankrollSchema
{
    public const string Name = "bankroll";
}

/// <summary>One row per user: a bankroll has one starting point and one rule.</summary>
internal sealed class BankrollSettingsRecord
{
    public Guid UserId { get; set; }

    public decimal StartingAmount { get; set; }

    public DateTimeOffset StartedOn { get; set; }

    public BuyInRule Rule { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class BankrollMovementRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public BankrollMovementKind Kind { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class BankrollSettingsConfiguration : IEntityTypeConfiguration<BankrollSettingsRecord>
{
    public void Configure(EntityTypeBuilder<BankrollSettingsRecord> builder)
    {
        builder.ToTable("settings", BankrollSchema.Name);
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.StartingAmount).HasPrecision(12, 2);
        builder.Property(s => s.Rule).HasConversion<int>();
        builder.HasOne<User>().WithOne().HasForeignKey<BankrollSettingsRecord>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankrollMovementConfiguration : IEntityTypeConfiguration<BankrollMovementRecord>
{
    public void Configure(EntityTypeBuilder<BankrollMovementRecord> builder)
    {
        builder.ToTable("movements", BankrollSchema.Name);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Kind).HasConversion<int>();
        builder.Property(m => m.Amount).HasPrecision(12, 2);
        builder.Property(m => m.Note).HasMaxLength(BankrollService.MaxNoteLength);
        builder.HasIndex(m => new { m.UserId, m.OccurredAt });
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankrollStore(PokerCoachDbContext db, TimeProvider time) : IBankrollStore
{
    public async Task<BankrollSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<BankrollSettingsRecord>().AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new BankrollSettings(s.StartingAmount, s.StartedOn, s.Rule))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Upsert in one statement: two tabs saving at once must not fail on the primary key.
    /// </summary>
    public async Task SaveSettingsAsync(Guid userId, BankrollSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var now = time.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO bankroll.settings (user_id, starting_amount, started_on, rule, updated_at)
            VALUES ({userId}, {settings.StartingAmount}, {settings.StartedOn}, {(int)settings.Rule}, {now})
            ON CONFLICT (user_id) DO UPDATE
            SET starting_amount = EXCLUDED.starting_amount,
                started_on = EXCLUDED.started_on,
                rule = EXCLUDED.rule,
                updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<BankrollMovement>> ListMovementsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<BankrollMovementRecord>().AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.OccurredAt)
            .Select(m => new BankrollMovement(m.Id, m.Kind, m.Amount, m.OccurredAt, m.Note))
            .ToListAsync(cancellationToken);

    public async Task AddMovementAsync(Guid userId, BankrollMovement movement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(movement);
        db.Add(new BankrollMovementRecord
        {
            Id = movement.Id,
            UserId = userId,
            Kind = movement.Kind,
            Amount = movement.Amount,
            OccurredAt = movement.OccurredAt,
            Note = movement.Note,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>Scoped to the user: another user's id deletes nothing.</summary>
    public async Task<bool> DeleteMovementAsync(Guid userId, Guid movementId, CancellationToken cancellationToken) =>
        await db.Set<BankrollMovementRecord>()
            .Where(m => m.UserId == userId && m.Id == movementId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}
