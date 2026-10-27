using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Import;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Statistics;

internal sealed class OpponentStatsBackfillStore(PokerCoachDbContext db) : IOpponentStatsBackfillStore
{
    public async Task<Guid?> GetNextAccountToBackfillAsync(CancellationToken cancellationToken)
    {
        var accountId = await db.Set<OpponentStatsRecord>()
            .AsNoTracking()
            .Where(o => o.FirstSeenAt <= DateTimeOffset.UnixEpoch)
            .Select(o => o.PokerAccountId)
            .FirstOrDefaultAsync(cancellationToken);

        return accountId != Guid.Empty ? accountId : null;
    }

    public async Task DeleteStatsForAccountAsync(Guid pokerAccountId, CancellationToken cancellationToken) =>
        await db.Set<OpponentStatsRecord>()
            .Where(o => o.PokerAccountId == pokerAccountId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<BackfillHand>> GetHandsAsync(Guid pokerAccountId, Guid? afterHandId, int batchSize, CancellationToken cancellationToken)
    {
        var query = db.Set<HandRecord>().AsNoTracking()
            .Where(h => h.PokerAccountId == pokerAccountId);

        if (afterHandId is { } cursor)
            query = query.Where(h => h.Id.CompareTo(cursor) > 0);

        var rows = await query
            .OrderBy(h => h.Id)
            .Select(h => new { h.Id, h.ButtonSeat, h.BigBlind, h.Details, h.HeroCards, h.StartedAt, h.TournamentId })
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var tournamentIds = rows.Where(r => r.TournamentId is not null).Select(r => r.TournamentId!.Value).Distinct().ToList();
        var tournaments = tournamentIds.Count > 0
            ? await db.Set<TournamentRecord>().AsNoTracking()
                .Where(t => tournamentIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.ExternalTournamentId, cancellationToken)
            : new Dictionary<Guid, string>();

        return rows.Select(r => new BackfillHand(
            r.Id,
            HandFactsStore.ToAnalysis(r.ButtonSeat, r.BigBlind, r.Details, r.HeroCards),
            r.StartedAt,
            r.TournamentId is { } tid && tournaments.TryGetValue(tid, out var extId) ? extId : null
        )).ToList();
    }

    public Task MarkAccountBackfilledAsync(Guid pokerAccountId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
