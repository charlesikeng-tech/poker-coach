using PokerCoach.Application.Leaks;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Progress;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Progress;

public interface IWeeklyPlanStore
{
    Task<WeeklyPlan?> GetAsync(Guid userId, DateOnly weekStart, GameType gameType, TournamentSubFormat? subFormat, CancellationToken cancellationToken);

    /// <summary>Stores the plan unless the week already has one (two tabs at once): the first one wins.</summary>
    Task AddIfAbsentAsync(Guid userId, WeeklyPlan plan, CancellationToken cancellationToken);

    /// <summary>Removes the week's plan, so the next read builds a new one.</summary>
    Task DeleteAsync(Guid userId, DateOnly weekStart, GameType gameType, TournamentSubFormat? subFormat, CancellationToken cancellationToken);

    /// <summary>Plans of weeks before <paramref name="before"/>, most recent first.</summary>
    Task<IReadOnlyList<WeeklyPlan>> ListBeforeAsync(Guid userId, DateOnly before, GameType gameType, TournamentSubFormat? subFormat, int count, CancellationToken cancellationToken);

    /// <summary>User IDs that have at least one plan for <paramref name="weekStart"/> (any game type), deduplicated.</summary>
    Task<IReadOnlyList<Guid>> ListUserIdsWithPlanAsync(DateOnly weekStart, CancellationToken cancellationToken);
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
public sealed class ProgressService(IWeeklyPlanStore plans, ILeakService leaks, ITrainingStore training, TimeProvider time)
{
    /// <summary>Below this many baseline hands, a plan would rest on noise: none is made.</summary>
    public const int MinBaselineHands = 300;

    public const int HistoryWeeks = 6;

    public Task<ProgressReport> GetAsync(Guid userId, StakesLevel stakesLevel, CancellationToken cancellationToken) =>
        GetAsync(userId, GameType.Tournament, null, stakesLevel, cancellationToken);

    public Task<ProgressReport> GetAsync(Guid userId, GameType gameType, StakesLevel stakesLevel, CancellationToken cancellationToken) =>
        GetAsync(userId, gameType, null, stakesLevel, cancellationToken);

    public async Task<ProgressReport> GetAsync(Guid userId, GameType gameType, TournamentSubFormat? subFormat, StakesLevel stakesLevel, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var week = WeeklyPlanner.WeekStart(now);

        var plan = await plans.GetAsync(userId, week, gameType, subFormat, cancellationToken);
        var baselineHands = 0;
        if (plan is null)
        {
            var analysis = await leaks.GetAsync(userId, null, now.AddDays(-WeeklyPlanner.BaselineDays), null, stakesLevel, cancellationToken, gameType, subFormat);
            baselineHands = analysis.Hands;
            if (analysis.Hands >= MinBaselineHands && analysis.PendingHands == 0)
            {
                await plans.AddIfAbsentAsync(
                    userId,
                    new WeeklyPlan(week, gameType, analysis.Format, WeeklyPlanner.Choose(analysis.Report), analysis.ReferenceVersion, now, subFormat),
                    cancellationToken);
                plan = await plans.GetAsync(userId, week, gameType, subFormat, cancellationToken);
            }
        }

        var current = plan is null ? null : await MeasureAsync(userId, plan, cancellationToken);
        var (attempts, correct) = await training.CountSinceAsync(userId, WeeklyPlanner.Start(week), null, cancellationToken);

        var history = new List<WeekReport>();
        foreach (var past in await plans.ListBeforeAsync(userId, week, gameType, subFormat, HistoryWeeks, cancellationToken))
        {
            history.Add(await MeasureAsync(userId, past, cancellationToken));
        }

        return new ProgressReport(week, current, baselineHands, attempts, correct, WeeklyPlanner.DrillGoal, history);
    }

    /// <summary>Drops this week's plan and builds it again (after a big import, for instance).</summary>
    public Task<ProgressReport> RebuildAsync(Guid userId, StakesLevel stakesLevel, CancellationToken cancellationToken) =>
        RebuildAsync(userId, GameType.Tournament, null, stakesLevel, cancellationToken);

    public Task<ProgressReport> RebuildAsync(Guid userId, GameType gameType, StakesLevel stakesLevel, CancellationToken cancellationToken) =>
        RebuildAsync(userId, gameType, null, stakesLevel, cancellationToken);

    public async Task<ProgressReport> RebuildAsync(Guid userId, GameType gameType, TournamentSubFormat? subFormat, StakesLevel stakesLevel, CancellationToken cancellationToken)
    {
        await plans.DeleteAsync(userId, WeeklyPlanner.WeekStart(time.GetUtcNow()), gameType, subFormat, cancellationToken);
        return await GetAsync(userId, gameType, subFormat, stakesLevel, cancellationToken);
    }

    private async Task<WeekReport> MeasureAsync(Guid userId, WeeklyPlan plan, CancellationToken cancellationToken)
    {
        var start = WeeklyPlanner.Start(plan.WeekStart);
        var counts = await leaks.CountAsync(userId, plan.Format, start, start.AddDays(7), cancellationToken, plan.GameType, plan.SubFormat);
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
