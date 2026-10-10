using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Analytics;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Platform;

internal sealed class FeatureUsageRecord
{
    public DateOnly Day { get; set; }

    public string Feature { get; set; } = string.Empty;

    public Guid UserId { get; set; }
}

internal sealed class FeatureUsageConfiguration : IEntityTypeConfiguration<FeatureUsageRecord>
{
    public void Configure(EntityTypeBuilder<FeatureUsageRecord> builder)
    {
        builder.ToTable("feature_usage", PlatformSchema.Name);
        builder.HasKey(u => new { u.Day, u.Feature, u.UserId });
        builder.Property(u => u.Feature).HasMaxLength(32);

        // Deleting an account removes its usage too: nothing about a user outlives the account.
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(u => u.UserId);
    }
}

internal sealed class FeatureUsageStore(PokerCoachDbContext db) : IFeatureUsageStore
{
    public async Task RecordAsync(Guid userId, DateOnly day, string feature, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO platform.feature_usage (day, feature, user_id)
            VALUES ({day}, {feature}, {userId})
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);

    public Task<int> PurgeBeforeAsync(DateOnly day, CancellationToken cancellationToken) =>
        db.Set<FeatureUsageRecord>().Where(u => u.Day < day).ExecuteDeleteAsync(cancellationToken);
}
