using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Statistics;

/// <summary>
/// The hero's facts for one hand (<see cref="HeroHandFacts"/>), one row per hand. Derived data: it can be
/// dropped and recomputed from <c>poker.hands.details</c> at any time (ADR-0006).
/// </summary>
internal sealed class HandHeroFactsRecord
{
    public Guid HandId { get; set; }

    public int FactsVersion { get; set; }

    public PokerPosition? Position { get; set; }

    public int PlayersDealt { get; set; }

    public decimal StackInBigBlinds { get; set; }

    public bool HadPreflopDecision { get; set; }

    public bool Vpip { get; set; }

    public bool Pfr { get; set; }

    public bool RfiOpportunity { get; set; }

    public bool Rfi { get; set; }

    public bool Limp { get; set; }

    public bool StealOpportunity { get; set; }

    public bool Steal { get; set; }

    public bool ThreeBetOpportunity { get; set; }

    public bool ThreeBet { get; set; }

    public bool FoldToThreeBetOpportunity { get; set; }

    public bool FoldToThreeBet { get; set; }

    public bool SawFlop { get; set; }

    public bool CbetFlopOpportunity { get; set; }

    public bool CbetFlop { get; set; }

    public bool WentToShowdown { get; set; }

    public bool WonAtShowdown { get; set; }

    public long NetChips { get; set; }

    public decimal NetBigBlinds { get; set; }

    public void Apply(HeroHandFacts facts, int version)
    {
        FactsVersion = version;
        Position = facts.Position;
        PlayersDealt = facts.PlayersDealt;
        StackInBigBlinds = facts.StackInBigBlinds;
        HadPreflopDecision = facts.HadPreflopDecision;
        Vpip = facts.Vpip;
        Pfr = facts.Pfr;
        RfiOpportunity = facts.RfiOpportunity;
        Rfi = facts.Rfi;
        Limp = facts.Limp;
        StealOpportunity = facts.StealOpportunity;
        Steal = facts.Steal;
        ThreeBetOpportunity = facts.ThreeBetOpportunity;
        ThreeBet = facts.ThreeBet;
        FoldToThreeBetOpportunity = facts.FoldToThreeBetOpportunity;
        FoldToThreeBet = facts.FoldToThreeBet;
        SawFlop = facts.SawFlop;
        CbetFlopOpportunity = facts.CbetFlopOpportunity;
        CbetFlop = facts.CbetFlop;
        WentToShowdown = facts.WentToShowdown;
        WonAtShowdown = facts.WonAtShowdown;
        NetChips = facts.NetChips;
        NetBigBlinds = facts.NetBigBlinds;
    }

    public HeroHandFacts ToDomain() => new(
        Position,
        PlayersDealt,
        StackInBigBlinds,
        HadPreflopDecision,
        Vpip,
        Pfr,
        RfiOpportunity,
        Rfi,
        Limp,
        StealOpportunity,
        Steal,
        ThreeBetOpportunity,
        ThreeBet,
        FoldToThreeBetOpportunity,
        FoldToThreeBet,
        SawFlop,
        CbetFlopOpportunity,
        CbetFlop,
        WentToShowdown,
        WonAtShowdown,
        NetChips,
        NetBigBlinds);
}

internal sealed class HandHeroFactsConfiguration : IEntityTypeConfiguration<HandHeroFactsRecord>
{
    public void Configure(EntityTypeBuilder<HandHeroFactsRecord> builder)
    {
        builder.ToTable("hand_hero_facts", PokerSchema.Name);
        builder.HasKey(f => f.HandId);
        builder.Property(f => f.HandId).ValueGeneratedNever();
        builder.Property(f => f.Position).HasConversion<int?>();
        builder.Property(f => f.StackInBigBlinds).HasPrecision(10, 2);
        builder.Property(f => f.NetBigBlinds).HasPrecision(12, 2);
        builder.HasIndex(f => f.FactsVersion);
        builder.HasOne<HandRecord>().WithOne().HasForeignKey<HandHeroFactsRecord>(f => f.HandId).OnDelete(DeleteBehavior.Cascade);
    }
}
