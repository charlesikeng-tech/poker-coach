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

    public async Task<IReadOnlyDictionary<string, Guid>> GetIdsAsync(
        Guid pokerAccountId,
        IReadOnlyCollection<string> opponentNames,
        CancellationToken cancellationToken)
    {
        if (opponentNames.Count == 0)
            return new Dictionary<string, Guid>();

        return await db.Set<OpponentStatsRecord>()
            .AsNoTracking()
            .Where(o => o.PokerAccountId == pokerAccountId && opponentNames.Contains(o.OpponentName))
            .ToDictionaryAsync(o => o.OpponentName, o => o.Id, cancellationToken);
    }

    private static Dictionary<string, OpponentStats> ToStats(List<OpponentStatsRecord> rows) =>
        rows.ToDictionary(
            r => r.OpponentName,
            r => new OpponentStats(
                r.Hands, r.Vpip, r.Pfr,
                r.RfiOpportunity, r.Rfi,
                r.StealOpportunity, r.Steal,
                r.ThreeBetOpportunity, r.ThreeBet,
                r.FoldToThreeBetOpportunity, r.FoldToThreeBet,
                r.SawFlop,
                r.CbetFlopOpportunity, r.CbetFlop,
                r.FoldToCbetFlopOpportunity, r.FoldToCbetFlop,
                r.WentToShowdown, r.WonAtShowdown,
                r.PostflopAggressive, r.PostflopDecisions),
            StringComparer.Ordinal);
}
