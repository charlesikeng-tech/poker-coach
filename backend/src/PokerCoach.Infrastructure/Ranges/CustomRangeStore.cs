using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Ranges;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Ranges;

internal static class RangesSchema
{
    public const string Name = "ranges";
}

internal sealed class CustomOpeningRangeConfiguration : IEntityTypeConfiguration<CustomOpeningRange>
{
    public void Configure(EntityTypeBuilder<CustomOpeningRange> builder)
    {
        builder.ToTable("custom_opening_ranges", RangesSchema.Name);
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Format).HasConversion<int>();
        builder.Property(r => r.Band).HasConversion<int>();
        builder.Property(r => r.Position).HasConversion<int>();
        builder.Property(r => r.GameType).HasConversion<int>();
        builder.Property(r => r.Hands).HasColumnType("bytea").IsRequired();
        builder.HasIndex(r => new { r.UserId, r.Format, r.Band, r.Position, r.GameType }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomDefenceRangeConfiguration : IEntityTypeConfiguration<CustomDefenceRange>
{
    public void Configure(EntityTypeBuilder<CustomDefenceRange> builder)
    {
        builder.ToTable("custom_defence_ranges", RangesSchema.Name);
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Format).HasConversion<int>();
        builder.Property(r => r.Band).HasConversion<int>();
        builder.Property(r => r.Defender).HasConversion<int>();
        builder.Property(r => r.Opener).HasConversion<int>();
        builder.Property(r => r.GameType).HasConversion<int>();
        builder.Property(r => r.CallHands).HasColumnType("bytea").IsRequired();
        builder.Property(r => r.ThreeBetHands).HasColumnType("bytea").IsRequired();
        builder.HasIndex(r => new { r.UserId, r.Format, r.Band, r.Defender, r.Opener, r.GameType }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomRangeStore(PokerCoachDbContext db) : ICustomRangeStore
{
    public async Task<CustomOpeningRange?> FindOpeningAsync(
        Guid userId, TableFormat format, StackBand band, PokerPosition position, GameType gameType, CancellationToken cancellationToken) =>
        await db.Set<CustomOpeningRange>()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Format == format && r.Band == band && r.Position == position && r.GameType == gameType, cancellationToken);

    public async Task<IReadOnlyList<CustomOpeningRange>> ListOpeningsAsync(
        Guid userId, TableFormat format, StackBand band, GameType gameType, CancellationToken cancellationToken) =>
        await db.Set<CustomOpeningRange>()
            .Where(r => r.UserId == userId && r.Format == format && r.Band == band && r.GameType == gameType)
            .ToListAsync(cancellationToken);

    public async Task SaveOpeningAsync(CustomOpeningRange range, CancellationToken cancellationToken)
    {
        if (db.Entry(range).State == EntityState.Detached)
            db.Add(range);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<bool> DeleteOpeningAsync(
        Guid userId, TableFormat format, StackBand band, PokerPosition position, GameType gameType, CancellationToken cancellationToken) =>
        await db.Set<CustomOpeningRange>()
            .Where(r => r.UserId == userId && r.Format == format && r.Band == band && r.Position == position && r.GameType == gameType)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<CustomDefenceRange?> FindDefenceAsync(
        Guid userId, TableFormat format, StackBand band, PokerPosition defender, PokerPosition opener, GameType gameType, CancellationToken cancellationToken) =>
        await db.Set<CustomDefenceRange>()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Format == format && r.Band == band && r.Defender == defender && r.Opener == opener && r.GameType == gameType, cancellationToken);

    public async Task<IReadOnlyList<CustomDefenceRange>> ListDefencesAsync(
        Guid userId, TableFormat format, StackBand band, GameType gameType, CancellationToken cancellationToken) =>
        await db.Set<CustomDefenceRange>()
            .Where(r => r.UserId == userId && r.Format == format && r.Band == band && r.GameType == gameType)
            .ToListAsync(cancellationToken);

    public async Task SaveDefenceAsync(CustomDefenceRange range, CancellationToken cancellationToken)
    {
        if (db.Entry(range).State == EntityState.Detached)
            db.Add(range);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<bool> DeleteDefenceAsync(
        Guid userId, TableFormat format, StackBand band, PokerPosition defender, PokerPosition opener, GameType gameType, CancellationToken cancellationToken) =>
        await db.Set<CustomDefenceRange>()
            .Where(r => r.UserId == userId && r.Format == format && r.Band == band && r.Defender == defender && r.Opener == opener && r.GameType == gameType)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}
