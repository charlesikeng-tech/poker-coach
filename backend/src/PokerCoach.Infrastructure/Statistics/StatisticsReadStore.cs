using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Ranges;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Training;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Domain.Tournaments;
using PokerCoach.Infrastructure.Poker;
using PokerCoach.Infrastructure.Tournaments;

namespace PokerCoach.Infrastructure.Statistics;

/// <summary>One aggregate query (COUNT ... FILTER per flag, grouped by position): no hand leaves the database.</summary>
internal sealed class StatisticsReadStore(PokerCoachDbContext db) : IStatisticsReadStore, ICashPerformanceReadStore, ICashSessionReadStore, IRangeReadStore, IAllInReadStore, IRealSpotStore, IPlayedSizingStore
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
            .Where(x => x.TournamentId != null)
            .Select(x => new { TournamentId = x.TournamentId!.Value, x.Complete })
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

    public async Task<IReadOnlyList<DefenceHoldingCount>> CountDefencesAsync(
        Guid userId,
        StatisticsFilter filter,
        int factsVersion,
        CancellationToken cancellationToken)
    {
        var rows = await Filtered(userId, filter, factsVersion)
            .Where(x => x.F.FacingOpenFrom != null && x.F.Position != null && x.HeroCards != null)
            .GroupBy(x => new { x.F.Position, x.F.FacingOpenFrom, x.MaxSeats, x.F.PlayersDealt, x.HeroCards })
            .Select(g => new
            {
                g.Key.Position,
                g.Key.FacingOpenFrom,
                g.Key.MaxSeats,
                g.Key.PlayersDealt,
                g.Key.HeroCards,
                Dealt = g.Count(),
                Calls = g.Count(x => x.F.FacingOpenCall),
                ThreeBets = g.Count(x => x.F.FacingOpenThreeBet),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new DefenceHoldingCount(r.Position!.Value, r.FacingOpenFrom!.Value, r.MaxSeats, r.PlayersDealt, r.HeroCards!, r.Dealt, r.Calls, r.ThreeBets))
            .ToList();
    }

    /// <summary>The hero's opens and 3-bets against a single open: a few columns per raise, a sixth of the hands at most.</summary>
    public async Task<IReadOnlyList<PlayedRaiseRow>> ListRaisesAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
        await Filtered(userId, filter, factsVersion)
            .Where(x => x.F.Position != null && (x.F.OpenToBigBlinds != null || x.F.ThreeBetToBigBlinds != null))
            .Select(x => new PlayedRaiseRow(
                x.F.HandId,
                x.StartedAt,
                x.F.Position!.Value,
                x.F.PlayersDealt,
                x.F.StackInBigBlinds,
                x.F.OpenToBigBlinds,
                x.F.OpenAllIn,
                x.F.FacingOpenFrom,
                x.F.FacingOpenToBigBlinds,
                x.F.FacingOpenEffectiveStack,
                x.F.ThreeBetToBigBlinds,
                x.F.ThreeBetAllIn,
                x.HeroCards))
            .ToListAsync(cancellationToken);

    /// <summary>The hero's raise-first-in spots with known cards: a fifth of the hands at most, read whole.</summary>
    public async Task<IReadOnlyList<RealOpeningRow>> ListOpeningSpotsAsync(Guid userId, TableFormat format, DateTimeOffset since, int factsVersion, CancellationToken cancellationToken) =>
        await Filtered(userId, new StatisticsFilter(since, null, null, null, Format: format), factsVersion)
            .Where(x => x.F.RfiOpportunity && x.F.Position != null && x.HeroCards != null)
            .Select(x => new RealOpeningRow(
                x.F.HandId,
                x.StartedAt,
                x.F.Position!.Value,
                x.F.PlayersDealt,
                x.F.StackInBigBlinds,
                x.HeroCards!,
                x.F.Rfi,
                x.F.Limp))
            .ToListAsync(cancellationToken);

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
        var gameType = filter.GameType ?? GameType.Tournament;
        var query =
            from f in db.Set<HandHeroFactsRecord>().AsNoTracking()
            join h in db.Set<HandRecord>() on f.HandId equals h.Id
            join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
            join c in db.Set<TournamentCoverageRecord>() on h.TournamentId equals c.TournamentId into coverage
            from c in coverage.DefaultIfEmpty()
            where a.UserId == userId && a.ConfirmedAt != null && f.FactsVersion == factsVersion
                  && h.GameType == gameType
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
            var (firstLevel, lastLevel) = TournamentPhases.Levels(phase);
            query = lastLevel is { } top
                ? query.Where(x => x.Level >= firstLevel && x.Level <= top)
                : query.Where(x => x.Level >= firstLevel);
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

    public async Task<IReadOnlyList<CashHandRow>> ListCashHandsAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, int factsVersion, CancellationToken cancellationToken)
    {
        var filter = new StatisticsFilter(from, to, null, null, GameType: GameType.CashGame);
        return await Filtered(userId, filter, factsVersion)
            .Select(x => new CashHandRow(x.StartedAt, x.BigBlind, x.F.NetBigBlinds))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CashHandSummary>> ListCashHandSummariesAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, int factsVersion, CancellationToken cancellationToken)
    {
        var filter = new StatisticsFilter(from, to, null, null, GameType: GameType.CashGame);
        return await Filtered(userId, filter, factsVersion)
            .Select(x => new CashHandSummary(x.F.HandId, x.StartedAt, x.BigBlind, x.MaxSeats, x.F.NetBigBlinds, x.F.StackInBigBlinds, x.F.Position, x.HeroCards))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CashHandSummary>> ListSessionHandsAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, int factsVersion, CancellationToken cancellationToken)
    {
        var filter = new StatisticsFilter(from, to, null, null, GameType: GameType.CashGame);
        return await Filtered(userId, filter, factsVersion)
            .OrderBy(x => x.StartedAt)
            .Select(x => new CashHandSummary(x.F.HandId, x.StartedAt, x.BigBlind, x.MaxSeats, x.F.NetBigBlinds, x.F.StackInBigBlinds, x.F.Position, x.HeroCards))
            .ToListAsync(cancellationToken);
    }

    public async Task<HeroStatCounts> GetCashSessionStatsAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, int factsVersion, CancellationToken cancellationToken)
    {
        var filter = new StatisticsFilter(from, to, null, null, GameType: GameType.CashGame);
        var row = await Filtered(userId, filter, factsVersion)
            .GroupBy(_ => 1)
            .Select(g => new
            {
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
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return HeroStatCounts.Zero;
        return new HeroStatCounts(
            row.Hands, row.PreflopDecisions, row.Vpip, row.Pfr,
            row.RfiOpportunities, row.Rfi, row.Limp,
            row.StealOpportunities, row.Steal,
            row.ThreeBetOpportunities, row.ThreeBet,
            row.FoldToThreeBetOpportunities, row.FoldToThreeBet,
            row.SawFlop, row.CbetFlopOpportunities, row.CbetFlop,
            row.WentToShowdown, row.WonAtShowdown, row.NetBigBlinds,
            row.FoldToCbetFlopOpportunities, row.FoldToCbetFlop, row.RaiseCbetFlop,
            row.CbetTurnOpportunities, row.CbetTurn,
            row.CheckRaiseFlopOpportunities, row.CheckRaiseFlop,
            row.WonWhenSawFlop, row.PostflopAggressive, row.PostflopDecisions);
    }

    public Task<int> CountPendingCashAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) =>
        (from h in db.Set<HandRecord>()
         join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
         join f in db.Set<HandHeroFactsRecord>() on h.Id equals f.HandId into facts
         from f in facts.DefaultIfEmpty()
         where a.UserId == userId && a.ConfirmedAt != null && h.GameType == GameType.CashGame
               && (f == null || f.FactsVersion != factsVersion)
         select h.Id).CountAsync(cancellationToken);

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

        public Guid? TournamentId { get; init; }

        public string? HeroCards { get; init; }

        public int MaxSeats { get; init; }

        public long BigBlind { get; init; }

        public int Level { get; init; }

        public bool Complete { get; init; }
    }
}
