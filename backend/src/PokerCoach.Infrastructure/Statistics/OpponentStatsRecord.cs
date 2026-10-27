using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Statistics;

/// <summary>
/// Aggregate stats for one opponent as seen by one poker account. Counters are additive — upserted
/// at import time with <c>ON CONFLICT DO UPDATE SET col = col + excluded.col</c>. Derived data:
/// can be recomputed from <c>poker.hands.details</c>.
/// </summary>
internal sealed class OpponentStatsRecord
{
    public Guid Id { get; set; }

    public Guid PokerAccountId { get; set; }

    public string OpponentName { get; set; } = string.Empty;

    public int Hands { get; set; }

    public int Vpip { get; set; }

    public int Pfr { get; set; }

    public int ThreeBetOpportunity { get; set; }

    public int ThreeBet { get; set; }

    public int SawFlop { get; set; }

    public int WentToShowdown { get; set; }

    public int PostflopAggressive { get; set; }

    public int PostflopDecisions { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class OpponentStatsConfiguration : IEntityTypeConfiguration<OpponentStatsRecord>
{
    public void Configure(EntityTypeBuilder<OpponentStatsRecord> builder)
    {
        builder.ToTable("opponent_stats", PokerSchema.Name);
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.OpponentName).HasMaxLength(100).IsRequired();
        builder.HasIndex(o => new { o.PokerAccountId, o.OpponentName })
            .IsUnique()
            .HasDatabaseName("ix_opponent_stats_account_opponent");
        builder.HasOne<PokerAccountRecord>().WithMany().HasForeignKey(o => o.PokerAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
