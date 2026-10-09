using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Tests.Leaks;

public sealed class LeakServiceFormatTests
{
    // Opens 23 % from UTG: wide for a full-ring UTG (11–20 %), normal for a 6-max UTG (a lojack's 15–24 %).
    private static readonly HeroStatCounts Utg = new(
        1000, 1000, 230, 230, 400, 92, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0m);

    [Fact]
    public async Task A_six_max_utg_is_judged_like_a_full_ring_lojack_and_keeps_its_name()
    {
        var sixMax = await new LeakService(new Stub()).GetAsync(Guid.NewGuid(), TableFormat.SixMax, null, null, TestContext.Current.CancellationToken);
        var fullRing = await new LeakService(new Stub()).GetAsync(Guid.NewGuid(), TableFormat.FullRing, null, null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(sixMax.Report.Leaks, l => l.Stat == LeakStat.Rfi);
        Assert.Contains(fullRing.Report.Leaks, l => l.Stat == LeakStat.Rfi && l.Position == PokerPosition.Utg && l.Direction == LeakDirection.TooHigh);
        Assert.Equal(TableFormat.SixMax, sixMax.Format);
    }

    [Fact]
    public async Task Without_a_format_the_busiest_one_is_analysed()
    {
        var analysis = await new LeakService(new Stub(new FormatCounts(100, 900))).GetAsync(Guid.NewGuid(), null, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(TableFormat.FullRing, analysis.Format);
    }

    private sealed class Stub(FormatCounts? formats = null) : IStatisticsReadStore
    {
        public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(PokerPosition?, HeroStatCounts)>>([(PokerPosition.Utg, Utg)]);

        public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new StatisticsSample(10, 5));

        public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(formats ?? new FormatCounts(1000, 0));

        public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
