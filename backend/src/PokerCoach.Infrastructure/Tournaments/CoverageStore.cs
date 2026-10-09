using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Tournaments;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Statistics;

namespace PokerCoach.Infrastructure.Tournaments;

/// <summary>Derived data (ADR-0006 pattern): recomputable at any time from hands, facts and the summary.</summary>
internal sealed class TournamentCoverageRecord
{
    public Guid TournamentId { get; set; }

    public int CoverageVersion { get; set; }

    public DateTimeOffset ComputedAt { get; set; }

    public CoverageStatus Status { get; set; }

    public int HandCount { get; set; }

    public int? FirstLevel { get; set; }

    public int? LastLevel { get; set; }

    public int MissingHands { get; set; }

    public int StackBreaks { get; set; }

    public int EntriesSeen { get; set; }

    public bool? StartMissing { get; set; }

    public bool EndSeen { get; set; }

    public TournamentCoverage ToDomain() =>
        new(Status, HandCount, FirstLevel, LastLevel, MissingHands, StackBreaks, EntriesSeen, StartMissing, EndSeen);
}

internal sealed class TournamentCoverageConfiguration : IEntityTypeConfiguration<TournamentCoverageRecord>
{
    public void Configure(EntityTypeBuilder<TournamentCoverageRecord> builder)
    {
        builder.ToTable("tournament_coverage", PokerSchema.Name);
        builder.HasKey(c => c.TournamentId);
        builder.Property(c => c.TournamentId).ValueGeneratedNever();
        builder.Property(c => c.Status).HasConversion<int>();
        builder.HasOne<TournamentRecord>().WithOne().HasForeignKey<TournamentCoverageRecord>(c => c.TournamentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CoverageStore(PokerCoachDbContext db, TimeProvider time) : ICoverageStore
{
    public async Task<IReadOnlyList<StaleTournament>> GetStaleAsync(int coverageVersion, int factsVersion, int batchSize, CancellationToken cancellationToken)
    {
        var hands = db.Set<HandRecord>();
        var facts = db.Set<HandHeroFactsRecord>();
        var stale = await (
                from t in db.Set<TournamentRecord>().AsNoTracking()
                join a in db.Set<PokerAccountRecord>() on t.PokerAccountId equals a.Id
                join c in db.Set<TournamentCoverageRecord>() on t.Id equals c.TournamentId into coverage
                from c in coverage.DefaultIfEmpty()
                let handCount = hands.Count(h => h.TournamentId == t.Id)
                let factsPending = hands.Any(h => h.TournamentId == t.Id
                    && !facts.Any(f => f.HandId == h.Id && f.FactsVersion == factsVersion))
                where !factsPending
                    && (c == null
                        || c.CoverageVersion < coverageVersion
                        || c.HandCount != handCount
                        || (t.SummaryImportedAt != null && c.ComputedAt < t.SummaryImportedAt))
                orderby t.Id
                select new { t.Id, a.Room })
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return [];
        }

        var ids = stale.Select(t => t.Id).ToList();
        var points = (await (
                from h in hands.AsNoTracking()
                join f in facts on h.Id equals f.HandId
                where ids.Contains(h.TournamentId)
                select new { h.TournamentId, h.StartedAt, h.Level, h.ExternalHandId, h.HeroStack, f.NetChips })
            .ToListAsync(cancellationToken))
            .ToLookup(h => h.TournamentId, h => new StoredHandPoint(h.StartedAt, h.Level, h.ExternalHandId, h.HeroStack, h.NetChips));

        var entries = (await db.Set<TournamentEntryRecord>().AsNoTracking()
                .Where(e => ids.Contains(e.TournamentId))
                .OrderBy(e => e.EntryNumber)
                .Select(e => new { e.TournamentId, e.LateRegistration, e.FinishPosition })
                .ToListAsync(cancellationToken))
            .ToLookup(e => e.TournamentId);

        return stale
            .Select(t =>
            {
                var tournamentEntries = entries[t.Id].ToList();
                var summary = tournamentEntries.Count == 0
                    ? new SummaryFacts(null, null, null)
                    : new SummaryFacts(tournamentEntries.Count, tournamentEntries[0].LateRegistration, tournamentEntries[^1].FinishPosition);
                return new StaleTournament(t.Id, t.Room, summary, points[t.Id].ToList());
            })
            .ToList();
    }

    public async Task SaveAsync(IReadOnlyList<(Guid TournamentId, TournamentCoverage Coverage)> coverage, int version, CancellationToken cancellationToken)
    {
        var ids = coverage.Select(c => c.TournamentId).ToList();
        var existing = await db.Set<TournamentCoverageRecord>()
            .Where(c => ids.Contains(c.TournamentId))
            .ToDictionaryAsync(c => c.TournamentId, cancellationToken);
        var now = time.GetUtcNow();

        foreach (var (tournamentId, value) in coverage)
        {
            if (!existing.TryGetValue(tournamentId, out var record))
            {
                record = new TournamentCoverageRecord { TournamentId = tournamentId };
                db.Add(record);
            }

            record.CoverageVersion = version;
            record.ComputedAt = now;
            record.Status = value.Status;
            record.HandCount = value.HandCount;
            record.FirstLevel = value.FirstLevel;
            record.LastLevel = value.LastLevel;
            record.MissingHands = value.MissingHands;
            record.StackBreaks = value.StackBreaks;
            record.EntriesSeen = value.EntriesSeen;
            record.StartMissing = value.StartMissing;
            record.EndSeen = value.EndSeen;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}
