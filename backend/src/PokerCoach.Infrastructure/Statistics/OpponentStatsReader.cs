using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Hands;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Infrastructure.Persistence;

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

        return rows.ToDictionary(
            r => r.OpponentName,
            r => new OpponentStats(
                r.Hands, r.Vpip, r.Pfr,
                r.ThreeBetOpportunity, r.ThreeBet,
                r.SawFlop, r.WentToShowdown,
                r.PostflopAggressive, r.PostflopDecisions),
            StringComparer.Ordinal);
    }
}
