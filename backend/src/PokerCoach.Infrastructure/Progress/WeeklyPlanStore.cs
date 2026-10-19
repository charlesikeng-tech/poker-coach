using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Progress;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Progress;
using PokerCoach.Domain.Tournaments;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Progress;

internal static class ProgressSchema
{
    public const string Name = "progress";
}

/// <summary>One plan per user, week, game type and sub-format. Priorities are a small frozen list: stored as a JSON document.</summary>
internal sealed class WeeklyPlanRecord
{
    public Guid UserId { get; set; }

    public DateOnly WeekStart { get; set; }

    public GameType GameType { get; set; } = GameType.Tournament;

    /// <summary>
    /// DB sentinel: 0 = combined (no sub-format filter), 1 = Mtt, 2 = RebuyMtt, 3 = Sng.
    /// Stored as NOT NULL to keep the primary key well-defined. Maps to <see cref="SubFormat"/> in C#.
    /// </summary>
    public int SubFormatKey { get; set; }

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
        builder.Property(p => p.SubFormatKey).HasDefaultValue(0);
        builder.HasKey(p => new { p.UserId, p.WeekStart, p.GameType, p.SubFormatKey });
        builder.Property(p => p.GameType).HasConversion<int>();
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

    public async Task<WeeklyPlan?> GetAsync(Guid userId, DateOnly weekStart, GameType gameType, TournamentSubFormat? subFormat, CancellationToken cancellationToken)
    {
        var key = ToKey(subFormat);
        var record = await db.Set<WeeklyPlanRecord>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userId && p.WeekStart == weekStart && p.GameType == gameType && p.SubFormatKey == key, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task AddIfAbsentAsync(Guid userId, WeeklyPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var priorities = JsonSerializer.Serialize(plan.Priorities, Json);
        var key = ToKey(plan.SubFormat);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO progress.weekly_plans (user_id, week_start, game_type, sub_format_key, format, reference_version, priorities, created_at)
            VALUES ({userId}, {plan.WeekStart}, {(int)plan.GameType}, {key}, {(int)plan.Format}, {plan.ReferenceVersion}, CAST({priorities} AS jsonb), {plan.CreatedAt})
            ON CONFLICT (user_id, week_start, game_type, sub_format_key) DO NOTHING
            """,
            cancellationToken);
    }

    public Task DeleteAsync(Guid userId, DateOnly weekStart, GameType gameType, TournamentSubFormat? subFormat, CancellationToken cancellationToken)
    {
        var key = ToKey(subFormat);
        return db.Set<WeeklyPlanRecord>()
            .Where(p => p.UserId == userId && p.WeekStart == weekStart && p.GameType == gameType && p.SubFormatKey == key)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WeeklyPlan>> ListBeforeAsync(Guid userId, DateOnly before, GameType gameType, TournamentSubFormat? subFormat, int count, CancellationToken cancellationToken)
    {
        var key = ToKey(subFormat);
        var records = await db.Set<WeeklyPlanRecord>().AsNoTracking()
            .Where(p => p.UserId == userId && p.WeekStart < before && p.GameType == gameType && p.SubFormatKey == key)
            .OrderByDescending(p => p.WeekStart)
            .Take(count)
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Guid>> ListUserIdsWithPlanAsync(DateOnly weekStart, CancellationToken cancellationToken) =>
        await db.Set<WeeklyPlanRecord>().AsNoTracking()
            .Where(p => p.WeekStart == weekStart)
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    private static int ToKey(TournamentSubFormat? subFormat) => subFormat.HasValue ? (int)subFormat.Value : 0;

    private static TournamentSubFormat? FromKey(int key) => key == 0 ? null : (TournamentSubFormat)key;

    private static WeeklyPlan ToDomain(WeeklyPlanRecord record) => new(
        record.WeekStart,
        record.GameType,
        record.Format,
        JsonSerializer.Deserialize<List<PlanPriority>>(record.Priorities, Json) ?? [],
        record.ReferenceVersion,
        record.CreatedAt,
        FromKey(record.SubFormatKey));
}
