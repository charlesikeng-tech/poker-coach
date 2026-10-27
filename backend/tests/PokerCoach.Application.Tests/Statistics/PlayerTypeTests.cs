using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Application.Tests.Statistics;

public sealed class PlayerTypeTests
{
    [Fact]
    public void Too_few_hands_returns_unknown()
    {
        var stats = new OpponentStats(19, 8, 6, 5, 1, 4, 2, 3, 8);
        Assert.Equal(PlayerType.Unknown, stats.Classify());
    }

    [Fact]
    public void Nit_has_very_low_vpip()
    {
        var stats = new OpponentStats(100, 12, 10, 30, 5, 10, 5, 4, 10);
        Assert.Equal(PlayerType.Nit, stats.Classify());
    }

    [Fact]
    public void Tag_has_moderate_vpip_and_small_gap()
    {
        var stats = new OpponentStats(100, 20, 16, 30, 6, 15, 8, 5, 15);
        Assert.Equal(PlayerType.Tag, stats.Classify());
    }

    [Fact]
    public void Lag_has_high_vpip_and_small_gap()
    {
        var stats = new OpponentStats(100, 30, 24, 30, 10, 20, 10, 12, 25);
        Assert.Equal(PlayerType.Lag, stats.Classify());
    }

    [Fact]
    public void Calling_station_has_high_vpip_and_large_gap()
    {
        var stats = new OpponentStats(100, 40, 10, 30, 3, 25, 15, 3, 25);
        Assert.Equal(PlayerType.CallingStation, stats.Classify());
    }

    [Fact]
    public void Maniac_has_very_high_vpip_and_pfr()
    {
        var stats = new OpponentStats(100, 50, 35, 30, 15, 30, 15, 20, 30);
        Assert.Equal(PlayerType.Maniac, stats.Classify());
    }

    [Fact]
    public void Shark_has_balanced_stats_and_aggression()
    {
        var stats = new OpponentStats(100, 22, 19, 40, 10, 30, 15, 15, 30);
        Assert.Equal(PlayerType.Shark, stats.Classify());
    }

    [Fact]
    public void Rates_are_computed_correctly()
    {
        var stats = new OpponentStats(200, 50, 40, 60, 12, 30, 10, 20, 50);
        Assert.Equal(0.25m, stats.VpipRate);
        Assert.Equal(0.20m, stats.PfrRate);
        Assert.Equal(0.05m, stats.VpipPfrGap);
        Assert.Equal(0.20m, stats.ThreeBetRate);
        Assert.Equal(0.40m, stats.AggressionFrequency);
    }

    [Fact]
    public void Zero_hands_does_not_divide_by_zero()
    {
        var stats = new OpponentStats(0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(0m, stats.VpipRate);
        Assert.Equal(0m, stats.AggressionFrequency);
        Assert.Equal(PlayerType.Unknown, stats.Classify());
    }
}
