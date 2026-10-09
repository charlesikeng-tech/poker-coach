using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Tests.Statistics;

public sealed class AllInLuckServiceTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sums_actual_and_expected_in_big_blinds_oldest_first()
    {
        var store = new StubStore(
            AllIn(Day.AddHours(2), 200, equity: 0.8m, expected: 1200m, net: -2000), // cooler lost: −10 BB vs +6 BB
            AllIn(Day, 100, equity: 0.3m, expected: -400m, net: 1000));             // suckout: +10 BB vs −4 BB

        var report = await new AllInLuckService(store, new NoPending()).GetAsync(Guid.NewGuid(), Filter, TestContext.Current.CancellationToken);

        Assert.Equal(2, report.AllIns);
        Assert.Equal(1, report.Won);
        Assert.Equal(1.1m, report.ExpectedWins);
        Assert.Equal(0.55m, report.AverageEquity);
        Assert.Equal(0m, report.ActualBigBlinds);
        Assert.Equal(2m, report.ExpectedBigBlinds);
        Assert.Equal(-2m, report.Luck);
        Assert.Equal(new[] { 10m, 0m }, report.Curve.Select(p => p.ActualBigBlinds));
        Assert.Equal(new[] { -4m, 2m }, report.Curve.Select(p => p.ExpectedBigBlinds));
        Assert.Equal(new[] { -16m, 14m }, report.Swings.Select(s => s.Luck));
    }

    [Fact]
    public async Task No_all_in_no_figure()
    {
        var report = await new AllInLuckService(new StubStore(), new NoPending()).GetAsync(Guid.NewGuid(), Filter, TestContext.Current.CancellationToken);

        Assert.Equal(0, report.AllIns);
        Assert.Null(report.AverageEquity);
        Assert.Empty(report.Curve);
    }

    private static StatisticsFilter Filter => new(null, null, null, null);

    private static AllInHand AllIn(DateTimeOffset at, long bigBlind, decimal equity, decimal expected, long net) =>
        new(Guid.NewGuid(), Guid.NewGuid(), at, bigBlind, "AhKd", equity, expected, net);

    private sealed class StubStore(params AllInHand[] hands) : IAllInReadStore
    {
        public Task<IReadOnlyList<AllInHand>> ListAllInsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AllInHand>>(hands);
    }

    private sealed class NoPending : IStatisticsReadStore
    {
        public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
