using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Ranges;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Domain.Tournaments;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Tournaments;

namespace PokerCoach.Infrastructure.Statistics;

/// <summary>One aggregate query (COUNT ... FILTER per flag, grouped by position): no hand leaves the database.</summary>
internal sealed class StatisticsReadStore(PokerCoachDbContext db) : IStatisticsReadStore, IRangeReadStore, IAllInReadStore
{
    public async Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken)
    {
        // Within a format, positions are named by distance to the button (OpeningSeat): grouped with the
        // number of players dealt, then renamed and summed.
        var rows = await Filtered(userId, filter, factsVersion)
            .GroupBy(x => new { x.F.Position, x.F.PlayersDealt })
            .Select(g => new
            {
                g.Key.Position,
                g.Key.PlayersDealt,
                Hands = g.Count(),
                PreflopDecisions = g.Count(x => x.F.HadPreflopDecision),
                Vpip = g.Count(x => x.F.Vpip),
                Pfr = g.Count(x => x.F.Pfr),
                RfiOpportunities = g.Count(x => x.F.RfiOpportunity),
                Rfi = g.Count(x => x.F.Rfi),
                Limp = g.Count(x => x.F.Limp),
                StealOpportunities = g.Count(x => x.F.StealOpportunity),
                Steal = g.Count(x => x.F.Steal),
                ThreeBetOpportunities = g.Count(x => x.F.ThreeBetOpportunity),
                ThreeBet = g.Count(x => x.F.ThreeBet),
                FoldToThreeBetOpportunities = g.Count(x => x.F.FoldToThreeBetOpportunity),
                FoldToThreeBet = g.Count(x => x.F.FoldToThreeBet),
                SawFlop = g.Count(x => x.F.SawFlop),
                CbetFlopOpportunities = g.Count(x => x.F.CbetFlopOpportunity),
                CbetFlop = g.Count(x => x.F.CbetFlop),
                WentToShowdown = g.Count(x => x.F.WentToShowdown),
                WonAtShowdown = g.Count(x => x.F.WonAtShowdown),
                NetBigBlinds = g.Sum(x => x.F.NetBigBlinds),
                FoldToCbetFlopOpportunities = g.Count(x => x.F.FoldToCbetFlopOpportunity),
                FoldToCbetFlop = g.Count(x => x.F.FoldToCbetFlop),
                RaiseCbetFlop = g.Count(x => x.F.RaiseCbetFlop),
                CbetTurnOpportunities = g.Count(x => x.F.CbetTurnOpportunity),
                CbetTurn = g.Count(x => x.F.CbetTurn),
                CheckRaiseFlopOpportunities = g.Count(x => x.F.CheckRaiseFlopOpportunity),
                CheckRaiseFlop = g.Count(x => x.F.CheckRaiseFlop),
                WonWhenSawFlop = g.Count(x => x.F.WonWhenSawFlop),
                PostflopAggressive = g.Sum(x => x.F.PostflopAggressive),
                PostflopDecisions = g.Sum(x => x.F.PostflopDecisions),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => (
                Position: r.Position is { } p && filter.Format is { } format ? OpeningSeat.Canonical(p, r.PlayersDealt, format) : r.Position,
                Counts: new HeroStatCounts(
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
                    r.NetBigBlinds,
                    r.FoldToCbetFlopOpportunities,
                    r.FoldToCbetFlop,
                    r.RaiseCbetFlop,
                    r.CbetTurnOpportunities,
                    r.CbetTurn,
                    r.CheckRaiseFlopOpportunities,
                    r.CheckRaiseFlop,
                    r.WonWhenSawFlop,
                    r.PostflopAggressive,
                    r.PostflopDecisions)))
            .GroupBy(r => r.Position)
            .Select(g => (g.Key, g.Aggregate(HeroStatCounts.Zero, (total, r) => total.Add(r.Counts))))
            .ToList();
    }

    /// <summary>One aggregate query grouped by year and month (timestamps are UTC: Npgsql reads them AT TIME ZONE UTC).</summary>
    public async Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken)
    {
        var rows = await Filtered(userId, filter, factsVersion)
            .GroupBy(x => new { x.StartedAt.Year, x.StartedAt.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Hands = g.Count(),
                PreflopDecisions = g.Count(x => x.F.HadPreflopDecision),
                Vpip = g.Count(x => x.F.Vpip),
                Pfr = g.Count(x => x.F.Pfr),
                RfiOpportunities = g.Count(x => x.F.RfiOpportunity),
                Rfi = g.Count(x => x.F.Rfi),
                Limp = g.Count(x => x.F.Limp),
                StealOpportunities = g.Count(x => x.F.StealOpportunity),
                Steal = g.Count(x => x.F.Steal),
                ThreeBetOpportunities = g.Count(x => x.F.ThreeBetOpportunity),
                ThreeBet = g.Count(x => x.F.ThreeBet),
                FoldToThreeBetOpportunities = g.Count(x => x.F.FoldToThreeBetOpportunity),
                FoldToThreeBet = g.Count(x => x.F.FoldToThreeBet),
                SawFlop = g.Count(x => x.F.SawFlop),
                CbetFlopOpportunities = g.Count(x => x.F.CbetFlopOpportunity),
                CbetFlop = g.Count(x => x.F.CbetFlop),
                WentToShowdown = g.Count(x => x.F.WentToShowdown),
                WonAtShowdown = g.Count(x => x.F.WonAtShowdown),
                NetBigBlinds = g.Sum(x => x.F.NetBigBlinds),
                FoldToCbetFlopOpportunities = g.Count(x => x.F.FoldToCbetFlopOpportunity),
                FoldToCbetFlop = g.Count(x => x.F.FoldToCbetFlop),
                RaiseCbetFlop = g.Count(x => x.F.RaiseCbetFlop),
                CbetTurnOpportunities = g.Count(x => x.F.CbetTurnOpportunity),
                CbetTurn = g.Count(x => x.F.CbetTurn),
                CheckRaiseFlopOpportunities = g.Count(x => x.F.CheckRaiseFlopOpportunity),
                CheckRaiseFlop = g.Count(x => x.F.CheckRaiseFlop),
                WonWhenSawFlop = g.Count(x => x.F.WonWhenSawFlop),
                PostflopAggressive = g.Sum(x => x.F.PostflopAggressive),
                PostflopDecisions = g.Sum(x => x.F.PostflopDecisions),
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .Select(r => (new DateOnly(r.Year, r.Month, 1), new HeroStatCounts(
                r.Hands, r.PreflopDecisions, r.Vpip, r.Pfr, r.RfiOpportunities, r.Rfi, r.Limp, r.StealOpportunities, r.Steal,
                r.ThreeBetOpportunities, r.ThreeBet, r.FoldToThreeBetOpportunities, r.FoldToThreeBet, r.SawFlop,
                r.CbetFlopOpportunities, r.CbetFlop, r.WentToShowdown, r.WonAtShowdown, r.NetBigBlinds,
                r.FoldToCbetFlopOpportunities, r.FoldToCbetFlop, r.RaiseCbetFlop, r.CbetTurnOpportunities, r.CbetTurn,
                r.CheckRaiseFlopOpportunities, r.CheckRaiseFlop, r.WonWhenSawFlop, r.PostflopAggressive, r.PostflopDecisions)))
            .ToList();
    }

    public async Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken)
    {
        var counts = await Filtered(userId, filter with { Format = null }, factsVersion)
            .GroupBy(x => x.MaxSeats <= 6)
            .Select(g => new { SixMax = g.Key, Hands = g.Count() })
            .ToListAsync(cancellationToken);
        return new FormatCounts(
            counts.Where(c => c.SixMax).Sum(c => c.Hands),
            counts.Where(c => !c.SixMax).Sum(c => c.Hands));
    }

    public async Task<StatisticsSample> CountTournamentsAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken)
    {
        var tournaments = await Filtered(userId, filter, factsVersion)
            .Select(x => new { x.TournamentId, x.Complete })
            .Distinct()
            .ToListAsync(cancellationToken);
        return new StatisticsSample(tournaments.Count, tournaments.Count(t => t.Complete));
    }

    /// <summary>One query grouped by position, table size and exact holding: a few thousand rows at most.</summary>
    public async Task<IReadOnlyList<OpeningHoldingCount>> CountOpeningsAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken)
    {
        var rows = await Filtered(userId, filter, factsVersion)
            .Where(x => x.F.RfiOpportunity && x.F.Position != null && x.HeroCards != null)
            .GroupBy(x => new { x.F.Position, x.MaxSeats, x.F.PlayersDealt, x.HeroCards })
            .Select(g => new
            {
                g.Key.Position,
                g.Key.MaxSeats,
                g.Key.PlayersDealt,
                g.Key.HeroCards,
                Dealt = g.Count(),
                Opens = g.Count(x => x.F.Rfi),
                Limps = g.Count(x => x.F.Limp),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new OpeningHoldingCount(r.Position!.Value, r.MaxSeats, r.PlayersDealt, r.HeroCards!, r.Dealt, r.Opens, r.Limps))
            .ToList();
    }

    /// <summary>Only rows with an all-in figure: a few per hundred hands, read whole.</summary>
    public async Task<IReadOnlyList<AllInHand>> ListAllInsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
        await Filtered(userId, filter, factsVersion)
            .Where(x => x.F.AllInExpectedNetChips != null && x.F.AllInEquity != null)
            .OrderBy(x => x.StartedAt)
            .Select(x => new AllInHand(
                x.F.HandId,
                x.TournamentId,
                x.StartedAt,
                x.BigBlind,
                x.HeroCards,
                x.F.AllInEquity!.Value,
                x.F.AllInExpectedNetChips!.Value,
                x.F.NetChips))
            .ToListAsync(cancellationToken);

    /// <summary>The user's analyzed hands within the filter, with whether their tournament's history is complete.</summary>
    private IQueryable<FilteredHand> Filtered(Guid userId, StatisticsFilter filter, int factsVersion)
    {
        var query =
            from f in db.Set<HandHeroFactsRecord>().AsNoTracking()
            join h in db.Set<HandRecord>() on f.HandId equals h.Id
            join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
            join c in db.Set<TournamentCoverageRecord>() on h.TournamentId equals c.TournamentId into coverage
            from c in coverage.DefaultIfEmpty()
            where a.UserId == userId && a.ConfirmedAt != null && f.FactsVersion == factsVersion
            select new FilteredHand
            {
                F = f,
                StartedAt = h.StartedAt,
                TournamentId = h.TournamentId,
                HeroCards = h.HeroCards,
                MaxSeats = h.MaxSeats,
                BigBlind = h.BigBlind,
                Level = h.Level,
                Complete = c != null && c.CoverageVersion == TournamentCoverage.Version && c.Status == CoverageStatus.Complete,
            };

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
            query = query.Where(x => x.F.StackInBigBlinds >= min);
        }

        if (filter.MaxStackBigBlinds is { } max)
        {
            query = query.Where(x => x.F.StackInBigBlinds < max);
        }

        if (filter.Phase is { } phase)
        {
            var (min, max) = TournamentPhases.Levels(phase);
            query = max is { } top
                ? query.Where(x => x.Level >= min && x.Level <= top)
                : query.Where(x => x.Level >= min);
        }

        if (filter.CompleteHistoryOnly)
        {
            query = query.Where(x => x.Complete);
        }

        // Same threshold as TableFormats.Of, written out so it translates to SQL.
        if (filter.Format == TableFormat.SixMax)
        {
            query = query.Where(x => x.MaxSeats <= 6);
        }
        else if (filter.Format == TableFormat.FullRing)
        {
            query = query.Where(x => x.MaxSeats > 6);
        }

        return query;
    }

    public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) =>
        (from h in db.Set<HandRecord>()
         join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
         join f in db.Set<HandHeroFactsRecord>() on h.Id equals f.HandId into facts
         from f in facts.DefaultIfEmpty()
         where a.UserId == userId && a.ConfirmedAt != null && (f == null || f.FactsVersion != factsVersion)
         select h.Id).CountAsync(cancellationToken);

    private sealed class FilteredHand
    {
        public required HandHeroFactsRecord F { get; init; }

        public DateTimeOffset StartedAt { get; init; }

        public Guid TournamentId { get; init; }

        public string? HeroCards { get; init; }

        public int MaxSeats { get; init; }

        public long BigBlind { get; init; }

        public int Level { get; init; }

        public bool Complete { get; init; }
    }
}
