using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Tournaments;

public sealed class PerformanceServiceTests
{
    [Fact]
    public async Task Cumulative_profit_follows_play_order_and_skips_unknown_results()
    {
        var store = new StubStore(
            Facts("LATER", new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), 5m, [new EntryOutcome(1151, null, null)]),
            Facts("FIRST", new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), 5m, [new EntryOutcome(11, 25.42m, 18.86m)]),
            Facts("NO SUMMARY", new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), 10m, []));

        var report = await new PerformanceService(store).GetAsync(Guid.NewGuid(), null, null, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "FIRST", "LATER" }, report.Curve.Select(p => p.Name));
        Assert.Equal(new[] { 39.28m, 34.28m }, report.Curve.Select(p => p.CumulativeProfit));
        Assert.Equal(new[] { 1, 2 }, report.Curve.Select(p => p.Index));
    }

    [Fact]
    public async Task Groups_by_disjoint_buy_in_bands_in_fixed_order()
    {
        var store = new StubStore(
            Facts("A", null, 25m, [new EntryOutcome(1, 100m, null)]),
            Facts("B", null, 5m, [new EntryOutcome(500, null, null)]),
            Facts("C", null, 10m, [new EntryOutcome(3, 20m, null)]),
            Facts("D", null, 4.99m, [new EntryOutcome(400, null, null)]));

        var report = await new PerformanceService(store).GetAsync(Guid.NewGuid(), null, null, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "upTo5", "from5To10", "over20" }, report.ByBuyIn.Select(g => g.Key));
        Assert.Equal(2, report.ByBuyIn[0].Figures.Tournaments);
        Assert.Equal(1m, report.ByBuyIn[1].Figures.ItmRate);
        Assert.Equal(0.5m, report.Totals.ItmRate);
    }

    [Theory]
    [InlineData(5.0, "upTo5")]
    [InlineData(5.01, "from5To10")]
    [InlineData(20.0, "from10To20")]
    [InlineData(20.5, "over20")]
    public void Buy_in_band_upper_bounds_are_inclusive(double buyIn, string band) =>
        Assert.Equal(band, PerformanceService.BuyInBand((decimal)buyIn));

    private static TournamentFacts Facts(string name, DateTimeOffset? startedAt, decimal buyInExcludingFee, IReadOnlyList<EntryOutcome> entries) =>
        new(Guid.NewGuid(), name, startedAt, "EUR", null, null, buyInExcludingFee, 0m, 100, 0, entries, "knockout", "normal");

    private sealed class StubStore(params TournamentFacts[] facts) : ITournamentReadStore
    {
        public Task<IReadOnlyList<TournamentFacts>> ListAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TournamentFacts>>(facts);
    }
}
