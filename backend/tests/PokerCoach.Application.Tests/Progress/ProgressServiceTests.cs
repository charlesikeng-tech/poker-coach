using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Progress;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Progress;

namespace PokerCoach.Application.Tests.Progress;

public sealed class ProgressServiceTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_plan_without_enough_recent_hands()
    {
        var stats = new StubStats(baseline: Counts(hands: 100, vpip: 50, decisions: 100), week: HeroStatCounts.Zero);
        var service = Service(stats, new MemoryPlans());

        var report = await service.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(report.Current);
        Assert.Equal(100, report.BaselineHands);
        Assert.Equal(new DateOnly(2026, 10, 5), report.WeekStart);
    }

    [Fact]
    public async Task The_plan_is_built_once_then_frozen_for_the_week_and_measured_on_its_hands()
    {
        var userId = Guid.NewGuid();
        var plans = new MemoryPlans();
        // Baseline: VPIP 50 % (reference 18–28 %): a confirmed leak. This week: 20 % over 40 hands.
        var stats = new StubStats(baseline: Counts(hands: 1000, vpip: 500, decisions: 1000), week: Counts(hands: 40, vpip: 8, decisions: 40));
        var service = Service(stats, plans);

        var first = await service.GetAsync(userId, TestContext.Current.CancellationToken);
        stats.Baseline = Counts(hands: 1000, vpip: 230, decisions: 1000); // fixed meanwhile: the plan does not move
        var second = await service.GetAsync(userId, TestContext.Current.CancellationToken);

        var vpip = Assert.Single(first.Current!.Priorities, p => p.Priority.Stat == LeakStat.Vpip);
        Assert.Equal(0.5m, vpip.Priority.BaselineRate);
        Assert.Equal(0.2m, vpip.Week.Rate);
        Assert.Equal(PriorityStatus.InRange, vpip.Week.Status);
        Assert.Equal(40, first.Current.Hands);
        Assert.Equal(first.Current.Plan.Priorities, second.Current!.Plan.Priorities);
        Assert.Equal(1, plans.Count);
    }

    [Fact]
    public async Task Rebuilding_replaces_this_weeks_plan_and_past_weeks_stay_in_history()
    {
        var userId = Guid.NewGuid();
        var plans = new MemoryPlans();
        plans.Add(userId, new WeeklyPlan(new DateOnly(2026, 9, 28), TableFormat.SixMax, [], ReferenceRanges.Version, Friday.AddDays(-10)));
        var stats = new StubStats(baseline: Counts(hands: 1000, vpip: 500, decisions: 1000), week: HeroStatCounts.Zero);
        var service = Service(stats, plans);

        await service.GetAsync(userId, TestContext.Current.CancellationToken);
        stats.Baseline = Counts(hands: 1000, vpip: 230, decisions: 1000);
        var rebuilt = await service.RebuildAsync(userId, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(rebuilt.Current!.Plan.Priorities, p => p.Stat == LeakStat.Vpip);
        Assert.Equal(new DateOnly(2026, 9, 28), Assert.Single(rebuilt.History).Plan.WeekStart);
    }

    private static ProgressService Service(StubStats stats, MemoryPlans plans) =>
        new(plans, new LeakService(stats), new NoTraining(), new FakeTimeProvider(Friday));

    /// <summary>Counts where VPIP and PFR move together (no gap leak) and nothing else is sampled.</summary>
    private static HeroStatCounts Counts(int hands, int vpip, int decisions) =>
        HeroStatCounts.Zero with { Hands = hands, PreflopDecisions = decisions, Vpip = vpip, Pfr = Math.Max(0, vpip - (decisions / 25)) };

    private sealed class StubStats(HeroStatCounts baseline, HeroStatCounts week) : IStatisticsReadStore
    {
        public HeroStatCounts Baseline { get; set; } = baseline;

        /// <summary>The week is asked with both bounds; the baseline without an upper one.</summary>
        private HeroStatCounts For(StatisticsFilter filter) => filter.To is null ? Baseline : week;

        public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(PokerPosition?, HeroStatCounts)>>([(null, For(filter))]);

        public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new StatisticsSample(10, 10));

        public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new FormatCounts(For(filter).Hands, 0));

        public Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>

            Task.FromResult<IReadOnlyList<(DateOnly, HeroStatCounts)>>([]);


        public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class MemoryPlans : IWeeklyPlanStore
    {
        private readonly Dictionary<(Guid, DateOnly), WeeklyPlan> plans = [];

        public int Count => plans.Count;

        public void Add(Guid userId, WeeklyPlan plan) => plans[(userId, plan.WeekStart)] = plan;

        public Task<WeeklyPlan?> GetAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken) =>
            Task.FromResult(plans.GetValueOrDefault((userId, weekStart)));

        public Task AddIfAbsentAsync(Guid userId, WeeklyPlan plan, CancellationToken cancellationToken)
        {
            plans.TryAdd((userId, plan.WeekStart), plan);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken)
        {
            plans.Remove((userId, weekStart));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WeeklyPlan>> ListBeforeAsync(Guid userId, DateOnly before, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WeeklyPlan>>(plans
                .Where(p => p.Key.Item1 == userId && p.Key.Item2 < before)
                .OrderByDescending(p => p.Key.Item2)
                .Take(count)
                .Select(p => p.Value)
                .ToList());
    }

    private sealed class NoTraining : ITrainingStore
    {
        public Task RecordAsync(Guid userId, DrillAttempt attempt, int referenceVersion, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<(int Attempts, int Correct)> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult((0, 0));

        public Task<IReadOnlyList<DrillAttempt>> RecentAsync(Guid userId, TableFormat format, DrillMode mode, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DrillAttempt>>([]);
    }
}
