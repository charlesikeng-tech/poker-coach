using PokerCoach.Application.Coaching;
using PokerCoach.Application.Progress;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Progress;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Coaching;

public sealed class WeekReviewServiceTests
{
    private static readonly PlanPriority ButtonOpens = new(LeakStat.Rfi, PokerPosition.Button, LeakDirection.TooLow, LeakConfidence.Confirmed, 0.20m, 200, 0.35m, 0.52m);

    [Fact]
    public void The_facts_carry_the_measured_status_and_warn_about_money_and_mid_week()
    {
        var facts = Facts(new PriorityProgress(9, 30, 0.30m, PriorityStatus.Improving), weekOver: false);

        var text = WeekReviewService.Facts(facts, Reviewed(facts));

        Assert.Contains("mid-week check", text, StringComparison.Ordinal);
        Assert.Contains("P1: Rfi from BTN, too low. Baseline 20 %, reference 35 %–52 %", text, StringComparison.Ordinal);
        Assert.Contains("30 % over 30 spots: still outside the range, but closer than the baseline", text, StringComparison.Ordinal);
        Assert.Contains("mostly variance", text, StringComparison.Ordinal);
        Assert.Contains("42 answers, 30 right; weekly goal 100", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_review_is_out_of_date_once_the_week_moves()
    {
        var before = Facts(new PriorityProgress(9, 30, 0.30m, PriorityStatus.Improving), weekOver: false);
        var moreSpots = Facts(new PriorityProgress(12, 40, 0.30m, PriorityStatus.Improving), weekOver: false);
        var over = Facts(new PriorityProgress(9, 30, 0.30m, PriorityStatus.Improving), weekOver: true);

        var fingerprint = WeekReviewService.Fingerprint(before, "fr");

        Assert.Equal(fingerprint, WeekReviewService.Fingerprint(before, "fr"));
        Assert.NotEqual(fingerprint, WeekReviewService.Fingerprint(moreSpots, "fr"));
        Assert.NotEqual(fingerprint, WeekReviewService.Fingerprint(over, "fr"));
        Assert.NotEqual(fingerprint, WeekReviewService.Fingerprint(before, "en"));
    }

    private static WeekReviewService.WeekFacts Facts(PriorityProgress progress, bool weekOver)
    {
        var plan = new WeeklyPlan(new DateOnly(2026, 10, 5), TableFormat.SixMax, [ButtonOpens], ReferenceRanges.Version, new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
        var week = new WeekReport(plan, [new PriorityReport(ButtonOpens, progress, null)], 400);
        var results = new PerformanceFigures(12, 12, 14, 2, 70m, 95m, 25m, 0.357m, 0.1429m);
        return new WeekReviewService.WeekFacts(week, weekOver, 5, 42, 30, 100, results);
    }

    private static List<ReviewedPriority> Reviewed(WeekReviewService.WeekFacts facts) =>
        facts.Week.Priorities
            .Select((p, i) => new ReviewedPriority($"P{i + 1}", p.Priority.Stat, p.Priority.Position, p.Priority.Direction, p.Week.Status, p.Week.Rate, p.Week.Opportunities, p.Priority.BaselineRate, p.Priority.Min, p.Priority.Max))
            .ToList();
}
