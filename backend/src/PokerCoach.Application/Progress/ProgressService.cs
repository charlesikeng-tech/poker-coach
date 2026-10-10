using PokerCoach.Application.Leaks;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Progress;

namespace PokerCoach.Application.Progress;

public interface IWeeklyPlanStore
{
    Task<WeeklyPlan?> GetAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken);

    /// <summary>Stores the plan unless the week already has one (two tabs at once): the first one wins.</summary>
    Task AddIfAbsentAsync(Guid userId, WeeklyPlan plan, CancellationToken cancellationToken);

    /// <summary>Removes the week's plan, so the next read builds a new one.</summary>
    Task DeleteAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken);

    /// <summary>Plans of weeks before <paramref name="before"/>, most recent first.</summary>
    Task<IReadOnlyList<WeeklyPlan>> ListBeforeAsync(Guid userId, DateOnly before, int count, CancellationToken cancellationToken);
}

/// <param name="Drill">The training-room drill for it; null when it is worked on hands and the coach.</param>
public sealed record PriorityReport(PlanPriority Priority, PriorityProgress Week, PriorityDrill? Drill);

/// <param name="Hands">Hands of the plan's format played that week (15+ BB).</param>
public sealed record WeekReport(WeeklyPlan Plan, IReadOnlyList<PriorityReport> Priorities, int Hands);

/// <param name="Current">Null when there is not enough history to build a plan yet.</param>
/// <param name="BaselineHands">Hands the current plan was (or would be) built from.</param>
/// <param name="DrillAttempts">Drill answers given this week.</param>
public sealed record ProgressReport(
    DateOnly WeekStart,
    WeekReport? Current,
    int BaselineHands,
    int DrillAttempts,
    int DrillCorrect,
    int DrillGoal,
    IReadOnlyList<WeekReport> History);

/// <summary>
/// The weekly plan (roadmap step 4): this week's two or three priorities from the leaks of the last
/// <see cref="WeeklyPlanner.BaselineDays"/> days, frozen for the week, with live progress on the week's
/// hands and the training room's drills.
/// </summary>
public sealed class ProgressService(IWeeklyPlanStore plans, LeakService leaks, ITrainingStore training, TimeProvider time)
{
    /// <summary>Below this many baseline hands, a plan would rest on noise: none is made.</summary>
    public const int MinBaselineHands = 300;

    public const int HistoryWeeks = 6;

    public async Task<ProgressReport> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var week = WeeklyPlanner.WeekStart(now);

        var plan = await plans.GetAsync(userId, week, cancellationToken);
        var baselineHands = 0;
        if (plan is null)
        {
            var analysis = await leaks.GetAsync(userId, null, now.AddDays(-WeeklyPlanner.BaselineDays), null, cancellationToken);
            baselineHands = analysis.Hands;
            if (analysis.Hands >= MinBaselineHands && analysis.PendingHands == 0)
            {
                await plans.AddIfAbsentAsync(
                    userId,
                    new WeeklyPlan(week, analysis.Format, WeeklyPlanner.Choose(analysis.Report), analysis.ReferenceVersion, now),
                    cancellationToken);
                plan = await plans.GetAsync(userId, week, cancellationToken);
            }
        }

        var current = plan is null ? null : await MeasureAsync(userId, plan, cancellationToken);
        var (attempts, correct) = await training.CountSinceAsync(userId, WeeklyPlanner.Start(week), null, cancellationToken);

        var history = new List<WeekReport>();
        foreach (var past in await plans.ListBeforeAsync(userId, week, HistoryWeeks, cancellationToken))
        {
            history.Add(await MeasureAsync(userId, past, cancellationToken));
        }

        return new ProgressReport(week, current, baselineHands, attempts, correct, WeeklyPlanner.DrillGoal, history);
    }

    /// <summary>Drops this week's plan and builds it again (after a big import, for instance).</summary>
    public async Task<ProgressReport> RebuildAsync(Guid userId, CancellationToken cancellationToken)
    {
        await plans.DeleteAsync(userId, WeeklyPlanner.WeekStart(time.GetUtcNow()), cancellationToken);
        return await GetAsync(userId, cancellationToken);
    }

    private async Task<WeekReport> MeasureAsync(Guid userId, WeeklyPlan plan, CancellationToken cancellationToken)
    {
        var start = WeeklyPlanner.Start(plan.WeekStart);
        var counts = await leaks.CountAsync(userId, plan.Format, start, start.AddDays(7), cancellationToken);
        var priorities = plan.Priorities
            .Select(p =>
            {
                var (made, opportunities) = counts.Measure(p.Stat, p.Position);
                return new PriorityReport(p, WeeklyPlanner.Assess(p, made, opportunities), WeeklyPlanner.DrillFor(p));
            })
            .ToList();
        return new WeekReport(plan, priorities, counts.Overall.Hands);
    }
}
