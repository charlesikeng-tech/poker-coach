using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker;

namespace PokerCoach.Infrastructure.Poker;

// Persistence shapes of the imported poker data. They carry no behaviour: writes go through the import
// store's SQL (insert-or-get on natural keys) and reads are projections. Domain aggregates will be
// introduced with the first feature that has invariants to protect beyond these constraints.

internal sealed class PokerAccountRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public PokerRoom Room { get; set; }

    /// <summary>Exactly as the room prints it (case and spaces kept): it is matched against hand histories.</summary>
    public string ScreenName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }
}

/// <summary>
/// Facts come from two sources: the hands (name, buy-in as printed on hands, first hand time) and the
/// summary file (everything else). Summary columns stay null until a summary is imported: null is UNKNOWN.
/// </summary>
internal sealed class TournamentRecord
{
    public Guid Id { get; set; }

    public Guid PokerAccountId { get; set; }

    public string ExternalTournamentId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Currency { get; set; }

    /// <summary>From hands: buy-in without the fee (prize pool and bounty parts together).</summary>
    public decimal? BuyInExcludingFee { get; set; }

    public decimal? Fee { get; set; }

    public DateTimeOffset? FirstHandAt { get; set; }

    public decimal? PrizePoolBuyIn { get; set; }

    public decimal? BountyBuyIn { get; set; }

    public int? RegisteredPlayers { get; set; }

    public string? Mode { get; set; }

    public string? TournamentType { get; set; }

    public string? Speed { get; set; }

    public string? FlightId { get; set; }

    public decimal? PrizePool { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? SummaryImportedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One buy-in of the player in a tournament (a re-entry is a second entry), from the summary file.
/// Results are per entry: ROI counts every buy-in. Replaced as a whole when a newer summary is imported.
/// </summary>
internal sealed class TournamentEntryRecord
{
    public Guid Id { get; set; }

    public Guid TournamentId { get; set; }

    /// <summary>1-based, in the order played.</summary>
    public int EntryNumber { get; set; }

    public bool LateRegistration { get; set; }

    public TimeSpan? PlayedDuration { get; set; }

    public int? FinishPosition { get; set; }

    /// <summary>Null when the summary prints no prize: unknown at import, interpreted by the performance module.</summary>
    public decimal? PrizeWinnings { get; set; }

    public decimal? BountyWinnings { get; set; }
}

/// <summary>
/// Columns hold what statistics filter and sort on; the full hand (seats, actions, board, showdown,
/// collections) is a versioned JSON document (<see cref="Import.HandDetailsDocument"/>), read whole.
/// </summary>
internal sealed class HandRecord
{
    public Guid Id { get; set; }

    public Guid PokerAccountId { get; set; }

    public Guid TournamentId { get; set; }

    public Guid? ImportedFileId { get; set; }

    public string ExternalHandId { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public int Level { get; set; }

    public long SmallBlind { get; set; }

    public long BigBlind { get; set; }

    public long? Ante { get; set; }

    public string TableName { get; set; } = string.Empty;

    public int MaxSeats { get; set; }

    public int ButtonSeat { get; set; }

    public int? HeroSeat { get; set; }

    public long? HeroStack { get; set; }

    /// <summary>Text form, e.g. "AhKd"; null when the hero was not dealt in.</summary>
    public string? HeroCards { get; set; }

    public long TotalPot { get; set; }

    public string Details { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

internal static class PokerSchema
{
    public const string Name = "poker";
}

internal sealed class PokerAccountConfiguration : IEntityTypeConfiguration<PokerAccountRecord>
{
    public void Configure(EntityTypeBuilder<PokerAccountRecord> builder)
    {
        builder.ToTable("poker_accounts", PokerSchema.Name);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Room).HasConversion<int>();
        builder.Property(a => a.ScreenName).HasMaxLength(100).IsRequired();
        builder.HasIndex(a => new { a.UserId, a.Room, a.ScreenName }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TournamentConfiguration : IEntityTypeConfiguration<TournamentRecord>
{
    public void Configure(EntityTypeBuilder<TournamentRecord> builder)
    {
        builder.ToTable("tournaments", PokerSchema.Name);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.ExternalTournamentId).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(8);
        builder.Property(t => t.Mode).HasMaxLength(32);
        builder.Property(t => t.TournamentType).HasMaxLength(32);
        builder.Property(t => t.Speed).HasMaxLength(32);
        builder.Property(t => t.FlightId).HasMaxLength(64);
        builder.Property(t => t.BuyInExcludingFee).HasPrecision(12, 2);
        builder.Property(t => t.Fee).HasPrecision(12, 2);
        builder.Property(t => t.PrizePoolBuyIn).HasPrecision(12, 2);
        builder.Property(t => t.BountyBuyIn).HasPrecision(12, 2);

        builder.Property(t => t.PrizePool).HasPrecision(14, 2);
        builder.HasIndex(t => new { t.PokerAccountId, t.ExternalTournamentId }).IsUnique();
        builder.HasOne<PokerAccountRecord>().WithMany().HasForeignKey(t => t.PokerAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TournamentEntryConfiguration : IEntityTypeConfiguration<TournamentEntryRecord>
{
    public void Configure(EntityTypeBuilder<TournamentEntryRecord> builder)
    {
        builder.ToTable("tournament_entries", PokerSchema.Name);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.PrizeWinnings).HasPrecision(12, 2);
        builder.Property(e => e.BountyWinnings).HasPrecision(12, 2);
        builder.HasIndex(e => new { e.TournamentId, e.EntryNumber }).IsUnique();
        builder.HasOne<TournamentRecord>().WithMany().HasForeignKey(e => e.TournamentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HandConfiguration : IEntityTypeConfiguration<HandRecord>
{
    public void Configure(EntityTypeBuilder<HandRecord> builder)
    {
        builder.ToTable("hands", PokerSchema.Name);
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.ExternalHandId).HasMaxLength(64).IsRequired();
        builder.Property(h => h.TableName).HasMaxLength(100).IsRequired();
        builder.Property(h => h.HeroCards).HasMaxLength(8);
        builder.Property(h => h.Details).HasColumnType("jsonb").IsRequired();

        // Deduplication key of hands: re-imports and overlapping files never create a second copy.
        builder.HasIndex(h => new { h.PokerAccountId, h.ExternalHandId }).IsUnique();
        builder.HasIndex(h => new { h.TournamentId, h.StartedAt });
        builder.HasIndex(h => h.ImportedFileId);

        builder.HasOne<PokerAccountRecord>().WithMany().HasForeignKey(h => h.PokerAccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TournamentRecord>().WithMany().HasForeignKey(h => h.TournamentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Import.ImportedFileRecord>().WithMany().HasForeignKey(h => h.ImportedFileId).OnDelete(DeleteBehavior.SetNull);
    }
}
