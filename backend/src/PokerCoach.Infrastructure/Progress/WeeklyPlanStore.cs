using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Progress;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Progress;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Progress;

internal static class ProgressSchema
{
    public const string Name = "progress";
}

/// <summary>One plan per user and week. Priorities are a small frozen list: stored as a JSON document.</summary>
internal sealed class WeeklyPlanRecord
{
    public Guid UserId { get; set; }

    public DateOnly WeekStart { get; set; }

    public TableFormat Format { get; set; }

    public int ReferenceVersion { get; set; }

    public string Priorities { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class WeeklyPlanConfiguration : IEntityTypeConfiguration<WeeklyPlanRecord>
{
    public void Configure(EntityTypeBuilder<WeeklyPlanRecord> builder)
    {
        builder.ToTable("weekly_plans", ProgressSchema.Name);
        // The key makes "one plan per week" a database rule, not a hope.
        builder.HasKey(p => new { p.UserId, p.WeekStart });
        builder.Property(p => p.Format).HasConversion<int>();
        builder.Property(p => p.Priorities).HasColumnType("jsonb").IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WeeklyPlanStore(PokerCoachDbContext db) : IWeeklyPlanStore
{
    // Enums by name: a stored plan stays readable if members are added.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<WeeklyPlan?> GetAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken)
    {
        var record = await db.Set<WeeklyPlanRecord>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userId && p.WeekStart == weekStart, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task AddIfAbsentAsync(Guid userId, WeeklyPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var priorities = JsonSerializer.Serialize(plan.Priorities, Json);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO progress.weekly_plans (user_id, week_start, format, reference_version, priorities, created_at)
            VALUES ({userId}, {plan.WeekStart}, {(int)plan.Format}, {plan.ReferenceVersion}, CAST({priorities} AS jsonb), {plan.CreatedAt})
            ON CONFLICT (user_id, week_start) DO NOTHING
            """,
            cancellationToken);
    }

    public Task DeleteAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken) =>
        db.Set<WeeklyPlanRecord>()
            .Where(p => p.UserId == userId && p.WeekStart == weekStart)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<WeeklyPlan>> ListBeforeAsync(Guid userId, DateOnly before, int count, CancellationToken cancellationToken)
    {
        var records = await db.Set<WeeklyPlanRecord>().AsNoTracking()
            .Where(p => p.UserId == userId && p.WeekStart < before)
            .OrderByDescending(p => p.WeekStart)
            .Take(count)
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    private static WeeklyPlan ToDomain(WeeklyPlanRecord record) => new(
        record.WeekStart,
        record.Format,
        JsonSerializer.Deserialize<List<PlanPriority>>(record.Priorities, Json) ?? [],
        record.ReferenceVersion,
        record.CreatedAt);
}
