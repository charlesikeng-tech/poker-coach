using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Tournaments;

public sealed class RoiIntervalTests
{
    [Fact]
    public void The_interval_is_centred_on_the_roi_shown_even_with_mixed_buy_ins()
    {
        // A €20 win and €5 losses: ROI is total profit over total cost, the interval must contain it.
        var results = Enumerable.Repeat((5m, -5m), 9).Append((20m, 60m)).ToList();

        var range = RoiInterval.Of(results)!;

        Assert.Equal(Math.Round(15m / 65m, 4), range.Roi);
        Assert.InRange(range.Roi, range.Low, range.High);
        Assert.Equal(range.Roi - range.Low, range.High - range.Roi, 3);
    }

    [Fact]
    public void Freerolls_are_left_out_and_one_result_has_no_width()
    {
        Assert.Null(RoiInterval.Of([(0m, 10m)]));

        var single = RoiInterval.Of([(10m, 20m), (0m, 5m)])!;
        Assert.Equal((1, 2m, 2m, 2m), (single.Tournaments, single.Roi, single.Low, single.High));
    }
}
