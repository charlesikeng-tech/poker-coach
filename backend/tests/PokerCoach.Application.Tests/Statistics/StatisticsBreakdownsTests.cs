using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Tests.Statistics;

public sealed class StatisticsBreakdownsTests
{
    [Theory]
    [InlineData(1, TournamentPhase.Early)]
    [InlineData(6, TournamentPhase.Early)]
    [InlineData(7, TournamentPhase.Middle)]
    [InlineData(12, TournamentPhase.Middle)]
    [InlineData(13, TournamentPhase.Late)]
    [InlineData(40, TournamentPhase.Late)]
    public void Phases_follow_the_blind_level(int level, TournamentPhase phase) =>
        Assert.Equal(phase, TournamentPhases.Of(level));

    [Fact]
    public async Task Each_phase_is_counted_apart_and_the_trend_covers_a_year_by_default()
    {
        var store = new RecordingStore();
        var service = new StatisticsService(store);

        var breakdowns = await service.GetBreakdownsAsync(
            Guid.NewGuid(),
            new StatisticsFilter(null, null, 15m, null),
            new DateTimeOffset(2026, 10, 9, 20, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { TournamentPhase.Early, TournamentPhase.Middle, TournamentPhase.Late }, breakdowns.ByPhase.Select(p => p.Phase));
        Assert.Equal(new[] { 10, 20, 30 }, breakdowns.ByPhase.Select(p => p.Line.Hands));
        Assert.Equal(new DateTimeOffset(2025, 11, 1, 0, 0, 0, TimeSpan.Zero), store.TrendFilter!.From);
        Assert.Equal(15m, store.TrendFilter.MinStackBigBlinds);
        Assert.Equal(TableFormat.SixMax, breakdowns.Format);
        Assert.Contains(breakdowns.References, r => r.Stat == LeakStat.Vpip);
        Assert.DoesNotContain(breakdowns.References, r => r.Position is not null);
        Assert.Equal(new DateOnly(2026, 9, 1), Assert.Single(breakdowns.ByMonth).Month);
    }

    private sealed class RecordingStore : IStatisticsReadStore
    {
        public StatisticsFilter? TrendFilter { get; private set; }

        public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken)
        {
            var hands = filter.Phase switch
            {
                TournamentPhase.Early => 10,
                TournamentPhase.Middle => 20,
                TournamentPhase.Late => 30,
                _ => 60,
            };
            return Task.FromResult<IReadOnlyList<(PokerPosition?, HeroStatCounts)>>([(null, HeroStatCounts.Zero with { Hands = hands })]);
        }

        public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new StatisticsSample(0, 0));

        public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new FormatCounts(60, 0));

        public Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken)
        {
            TrendFilter = filter;
            return Task.FromResult<IReadOnlyList<(DateOnly, HeroStatCounts)>>([(new DateOnly(2026, 9, 1), HeroStatCounts.Zero with { Hands = 60 })]);
        }

        public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
