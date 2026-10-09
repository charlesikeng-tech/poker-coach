using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.Infrastructure.Statistics;

/// <summary>One aggregate query (COUNT ... FILTER per flag, grouped by position): no hand leaves the database.</summary>
internal sealed class StatisticsReadStore(PokerCoachDbContext db) : IStatisticsReadStore
{
    public async Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken)
    {
        var query =
            from f in db.Set<HandHeroFactsRecord>().AsNoTracking()
            join h in db.Set<HandRecord>() on f.HandId equals h.Id
            join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
            where a.UserId == userId && a.ConfirmedAt != null && f.FactsVersion == factsVersion
            select new { f, h.StartedAt };

        if (filter.From is { } from)
        {
            query = query.Where(x => x.StartedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(x => x.StartedAt < to);
        }

        if (filter.MinStackBigBlinds is { } min)
        {
            query = query.Where(x => x.f.StackInBigBlinds >= min);
        }

        if (filter.MaxStackBigBlinds is { } max)
        {
            query = query.Where(x => x.f.StackInBigBlinds < max);
        }

        var rows = await query
            .GroupBy(x => x.f.Position)
            .Select(g => new
            {
                Position = g.Key,
                Hands = g.Count(),
                PreflopDecisions = g.Count(x => x.f.HadPreflopDecision),
                Vpip = g.Count(x => x.f.Vpip),
                Pfr = g.Count(x => x.f.Pfr),
                RfiOpportunities = g.Count(x => x.f.RfiOpportunity),
                Rfi = g.Count(x => x.f.Rfi),
                Limp = g.Count(x => x.f.Limp),
                StealOpportunities = g.Count(x => x.f.StealOpportunity),
                Steal = g.Count(x => x.f.Steal),
                ThreeBetOpportunities = g.Count(x => x.f.ThreeBetOpportunity),
                ThreeBet = g.Count(x => x.f.ThreeBet),
                FoldToThreeBetOpportunities = g.Count(x => x.f.FoldToThreeBetOpportunity),
                FoldToThreeBet = g.Count(x => x.f.FoldToThreeBet),
                SawFlop = g.Count(x => x.f.SawFlop),
                CbetFlopOpportunities = g.Count(x => x.f.CbetFlopOpportunity),
                CbetFlop = g.Count(x => x.f.CbetFlop),
                WentToShowdown = g.Count(x => x.f.WentToShowdown),
                WonAtShowdown = g.Count(x => x.f.WonAtShowdown),
                NetBigBlinds = g.Sum(x => x.f.NetBigBlinds),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => (r.Position, new HeroStatCounts(
                r.Hands,
                r.PreflopDecisions,
                r.Vpip,
                r.Pfr,
                r.RfiOpportunities,
                r.Rfi,
                r.Limp,
                r.StealOpportunities,
                r.Steal,
                r.ThreeBetOpportunities,
                r.ThreeBet,
                r.FoldToThreeBetOpportunities,
                r.FoldToThreeBet,
                r.SawFlop,
                r.CbetFlopOpportunities,
                r.CbetFlop,
                r.WentToShowdown,
                r.WonAtShowdown,
                r.NetBigBlinds)))
            .ToList();
    }

    public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) =>
        (from h in db.Set<HandRecord>()
         join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
         join f in db.Set<HandHeroFactsRecord>() on h.Id equals f.HandId into facts
         from f in facts.DefaultIfEmpty()
         where a.UserId == userId && a.ConfirmedAt != null && (f == null || f.FactsVersion != factsVersion)
         select h.Id).CountAsync(cancellationToken);
}
