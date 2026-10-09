using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Tournaments;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Tournaments;

/// <summary>Three projections (tournaments, entries, hand counts), joined in memory: no N+1, no large graph.</summary>
internal sealed class TournamentReadStore(PokerCoachDbContext db) : ITournamentReadStore
{
    public async Task<IReadOnlyList<TournamentFacts>> ListAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var query =
            from t in db.Set<TournamentRecord>().AsNoTracking()
            join a in db.Set<PokerAccountRecord>() on t.PokerAccountId equals a.Id
            join c in db.Set<TournamentCoverageRecord>() on t.Id equals c.TournamentId into coverage
            from c in coverage.DefaultIfEmpty()
            where a.UserId == userId && a.ConfirmedAt != null
            select new
            {
                Coverage = c,
                t.Id,
                t.Name,
                // The summary's start time when known; otherwise the first imported hand.
                StartedAt = t.StartedAt ?? t.FirstHandAt,
                t.Currency,
                t.PrizePoolBuyIn,
                t.BountyBuyIn,
                t.RebuyCost,
                t.AddonCost,
                t.BuyInExcludingFee,
                t.Fee,
                t.RegisteredPlayers,
                t.TournamentType,
                t.Speed,
                HasSummary = t.SummaryImportedAt != null,
            };

        if (from is { } lower)
        {
            query = query.Where(t => t.StartedAt >= lower);
        }

        if (to is { } upper)
        {
            query = query.Where(t => t.StartedAt < upper);
        }

        var tournaments = await query.OrderByDescending(t => t.StartedAt).ToListAsync(cancellationToken);
        if (tournaments.Count == 0)
        {
            return [];
        }

        var ids = tournaments.Select(t => t.Id).ToList();
        var entries = (await db.Set<TournamentEntryRecord>().AsNoTracking()
                .Where(e => ids.Contains(e.TournamentId))
                .OrderBy(e => e.EntryNumber)
                .Select(e => new { e.TournamentId, e.FinishPosition, e.PrizeWinnings, e.BountyWinnings, e.Rebuys, e.Addons })
                .ToListAsync(cancellationToken))
            .ToLookup(
                e => e.TournamentId,
                e => new EntryOutcome(e.FinishPosition, e.PrizeWinnings, e.BountyWinnings, e.Rebuys, e.Addons));

        var handCounts = await db.Set<HandRecord>().AsNoTracking()
            .Where(h => ids.Contains(h.TournamentId))
            .GroupBy(h => h.TournamentId)
            .Select(g => new { TournamentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TournamentId, g => g.Count, cancellationToken);

        return tournaments
            .Select(t => new TournamentFacts(
                t.Id,
                t.Name,
                t.StartedAt,
                t.Currency,
                t.PrizePoolBuyIn,
                t.BountyBuyIn,
                t.BuyInExcludingFee,
                t.Fee,
                t.RegisteredPlayers,
                handCounts.GetValueOrDefault(t.Id),
                t.HasSummary ? entries[t.Id].ToList() : [],
                t.TournamentType,
                t.Speed,
                // Outdated coverage is not shown: it is being recomputed.
                t.Coverage is { } stored && stored.CoverageVersion == TournamentCoverage.Version ? stored.ToDomain() : null,
                t.RebuyCost,
                t.AddonCost))
            .ToList();
    }
}
