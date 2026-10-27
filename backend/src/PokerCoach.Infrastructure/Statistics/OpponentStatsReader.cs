using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Hands;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Statistics;

internal sealed class OpponentStatsReader(PokerCoachDbContext db) : IOpponentStatsReader
{
    public async Task<IReadOnlyDictionary<string, OpponentStats>> GetAsync(
        Guid pokerAccountId,
        IReadOnlyCollection<string> opponentNames,
        CancellationToken cancellationToken)
    {
        if (opponentNames.Count == 0)
            return new Dictionary<string, OpponentStats>();

        var rows = await db.Set<OpponentStatsRecord>()
            .AsNoTracking()
            .Where(o => o.PokerAccountId == pokerAccountId && opponentNames.Contains(o.OpponentName))
            .ToListAsync(cancellationToken);

        return ToStats(rows);
    }

    public async Task<IReadOnlyDictionary<string, OpponentStats>> GetByUserAsync(
        Guid userId,
        IReadOnlyCollection<string> opponentNames,
        CancellationToken cancellationToken)
    {
        if (opponentNames.Count == 0)
            return new Dictionary<string, OpponentStats>();

        var rows = await (
                from o in db.Set<OpponentStatsRecord>().AsNoTracking()
                join a in db.Set<PokerAccountRecord>() on o.PokerAccountId equals a.Id
                where a.UserId == userId && opponentNames.Contains(o.OpponentName)
                select o)
            .ToListAsync(cancellationToken);

        return ToStats(rows);
    }

    private static Dictionary<string, OpponentStats> ToStats(List<OpponentStatsRecord> rows) =>
        rows.ToDictionary(
            r => r.OpponentName,
            r => new OpponentStats(
                r.Hands, r.Vpip, r.Pfr,
                r.ThreeBetOpportunity, r.ThreeBet,
                r.SawFlop, r.WentToShowdown,
                r.PostflopAggressive, r.PostflopDecisions),
            StringComparer.Ordinal);
}
