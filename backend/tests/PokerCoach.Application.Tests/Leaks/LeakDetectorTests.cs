using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;

namespace PokerCoach.Application.Tests.Leaks;

public sealed class LeakDetectorTests
{
    private static readonly IReadOnlyDictionary<PokerPosition, HeroStatCounts> NoPositions =
        new Dictionary<PokerPosition, HeroStatCounts>();

    [Fact]
    public void A_rate_far_outside_the_range_on_a_large_sample_is_a_confirmed_leak()
    {
        // VPIP 40 % over 1 000 hands, reference 18–28 %.
        var report = Detect(Counts(decisions: 1_000, vpip: 400, pfr: 200), [new(LeakStat.Vpip, null, 0.18m, 0.28m)]);

        var leak = Assert.Single(report.Leaks);
        Assert.Equal(LeakDirection.TooHigh, leak.Direction);
        Assert.Equal(LeakConfidence.Confirmed, leak.Confidence);
        Assert.Equal(0.4m, leak.Rate);
        Assert.True(leak.IntervalLow > 0.28m);
    }

    [Fact]
    public void The_same_rate_on_a_small_sample_is_only_possible()
    {
        var report = Detect(Counts(decisions: 40, vpip: 14, pfr: 8), [new(LeakStat.Vpip, null, 0.18m, 0.28m)]);

        Assert.Equal(LeakConfidence.Possible, Assert.Single(report.Leaks).Confidence);
    }

    [Fact]
    public void Too_few_opportunities_are_reported_not_judged()
    {
        var report = Detect(Counts(decisions: 12, vpip: 10, pfr: 0), [new(LeakStat.Vpip, null, 0.18m, 0.28m)]);

        Assert.Empty(report.Leaks);
        Assert.Equal(new UnderSampled(LeakStat.Vpip, null, 12, LeakDetector.MinOpportunities), Assert.Single(report.UnderSampled));
    }

    [Fact]
    public void A_rate_inside_the_range_is_fine()
    {
        Assert.Empty(Detect(Counts(decisions: 500, vpip: 120, pfr: 90), [new(LeakStat.Vpip, null, 0.18m, 0.28m)]).Leaks);
    }

    [Fact]
    public void Calling_much_more_than_raising_is_the_passive_leak()
    {
        // VPIP 26 % (fine) but PFR 10 %: 16 points of hands played by calling only.
        var report = Detect(Counts(decisions: 1_000, vpip: 260, pfr: 100), ReferenceRanges.LowStakesMtt.Where(r => r.Position is null && r.Stat is LeakStat.Vpip or LeakStat.VpipPfrGap).ToList());

        var leak = Assert.Single(report.Leaks);
        Assert.Equal(LeakStat.VpipPfrGap, leak.Stat);
        Assert.Equal(0.16m, leak.Rate);
    }

    [Fact]
    public void Opening_ranges_are_judged_per_position()
    {
        var button = HeroStatCounts.Zero with { Hands = 300, RfiOpportunities = 200, Rfi = 40 };
        var report = LeakDetector.Detect(
            button,
            new Dictionary<PokerPosition, HeroStatCounts> { [PokerPosition.Button] = button },
            [new(LeakStat.Rfi, PokerPosition.Button, 0.35m, 0.52m), new(LeakStat.Rfi, PokerPosition.Utg, 0.11m, 0.20m)]);

        var leak = Assert.Single(report.Leaks);
        Assert.Equal(PokerPosition.Button, leak.Position);
        Assert.Equal(LeakDirection.TooLow, leak.Direction);
        Assert.Equal(LeakConfidence.Confirmed, leak.Confidence);
        Assert.Equal(PokerPosition.Utg, Assert.Single(report.UnderSampled).Position);
    }

    [Theory]
    [InlineData(0, 10, 0.0, 0.2775)]
    [InlineData(50, 100, 0.4038, 0.5962)]
    public void Wilson_interval_matches_reference_values(int made, int n, double low, double high)
    {
        var (l, h) = LeakDetector.Wilson(made, n);

        Assert.Equal((decimal)low, l, 3);
        Assert.Equal((decimal)high, h, 3);
    }

    private static HeroStatCounts Counts(int decisions, int vpip, int pfr) =>
        HeroStatCounts.Zero with { Hands = decisions, PreflopDecisions = decisions, Vpip = vpip, Pfr = pfr };

    private static LeakReport Detect(HeroStatCounts overall, IReadOnlyList<ReferenceRange> ranges) =>
        LeakDetector.Detect(overall, NoPositions, ranges);
}
