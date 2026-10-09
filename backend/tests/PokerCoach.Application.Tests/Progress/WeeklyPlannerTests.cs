using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Progress;

namespace PokerCoach.Application.Tests.Progress;

public sealed class WeeklyPlannerTests
{
    [Theory]
    [InlineData("2026-10-05T00:00:00Z", "2026-10-05")] // Monday
    [InlineData("2026-10-09T21:00:00Z", "2026-10-05")] // Friday
    [InlineData("2026-10-11T23:59:59Z", "2026-10-05")] // Sunday
    [InlineData("2026-10-11T23:30:00-02:00", "2026-10-12")] // Monday 01:30 UTC
    public void Weeks_start_on_monday_utc(string now, string monday) =>
        Assert.Equal(DateOnly.Parse(monday, System.Globalization.CultureInfo.InvariantCulture), WeeklyPlanner.WeekStart(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Picks_three_leaks_in_report_order_with_two_seats_per_statistic_at_most()
    {
        var report = new LeakReport(
            [
                Leak(LeakStat.Rfi, PokerPosition.Button),
                Leak(LeakStat.Rfi, PokerPosition.Cutoff),
                Leak(LeakStat.Rfi, PokerPosition.Hijack),
                Leak(LeakStat.FoldToCbetFlop, null),
                Leak(LeakStat.ThreeBet, null),
            ],
            []);

        var priorities = WeeklyPlanner.Choose(report);

        Assert.Equal(new[] { LeakStat.Rfi, LeakStat.Rfi, LeakStat.FoldToCbetFlop }, priorities.Select(p => p.Stat));
        Assert.Equal(PokerPosition.Button, priorities[0].Position);
    }

    [Theory]
    [InlineData(5, 9, PriorityStatus.NotEnoughSpots)]
    [InlineData(45, 100, PriorityStatus.InRange)]
    [InlineData(60, 100, PriorityStatus.Improving)]
    [InlineData(70, 100, PriorityStatus.OffTrack)]
    public void Assesses_the_week_against_the_range_and_the_baseline(int made, int opportunities, PriorityStatus expected)
    {
        // Folds too often to c-bets: 65 % at baseline, reference 35–55 %.
        var priority = new PlanPriority(LeakStat.FoldToCbetFlop, null, LeakDirection.TooHigh, LeakConfidence.Confirmed, 0.65m, 200, 0.35m, 0.55m);

        Assert.Equal(expected, WeeklyPlanner.Assess(priority, made, opportunities).Status);
    }

    [Fact]
    public void Opening_leaks_point_to_the_drill_of_their_seat_and_postflop_ones_to_none()
    {
        var rfi = WeeklyPlanner.DrillFor(Priority(LeakStat.Rfi, PokerPosition.Cutoff))!;

        Assert.False(rfi.Defence);
        Assert.Equal(new[] { PokerPosition.Cutoff }, rfi.Positions);
        Assert.Null(WeeklyPlanner.DrillFor(Priority(LeakStat.CbetTurn, null)));
    }

    private static Leak Leak(LeakStat stat, PokerPosition? position) =>
        new(stat, position, LeakDirection.TooLow, LeakConfidence.Confirmed, 0.1m, 10, 100, 0.05m, 0.17m, new ReferenceRange(stat, position, 0.2m, 0.3m));

    private static PlanPriority Priority(LeakStat stat, PokerPosition? position) =>
        new(stat, position, LeakDirection.TooLow, LeakConfidence.Confirmed, 0.1m, 100, 0.2m, 0.3m);
}
