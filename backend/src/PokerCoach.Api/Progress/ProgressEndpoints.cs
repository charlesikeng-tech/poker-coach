using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Application.Progress;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Progress;

namespace PokerCoach.Api.Progress;

/// <param name="Positions">Seats to drill; empty: every seat.</param>
public sealed record PriorityDrillResponse(bool Defence, IReadOnlyList<PokerPosition> Positions);

/// <param name="BaselineRate">Rate over the last 90 days when the plan was made.</param>
/// <param name="WeekRate">Rate over this week's hands; null without any spot.</param>
/// <param name="Drill">Null when the leak is worked on hands and the coach rather than a drill.</param>
public sealed record PriorityResponse(
    LeakStat Stat,
    PokerPosition? Position,
    LeakDirection Direction,
    LeakConfidence Confidence,
    decimal BaselineRate,
    int BaselineOpportunities,
    decimal Min,
    decimal Max,
    int WeekMade,
    int WeekOpportunities,
    decimal? WeekRate,
    PriorityStatus Status,
    PriorityDrillResponse? Drill);

/// <param name="WeekStart">Monday of the week (UTC), "2026-10-05".</param>
/// <param name="Hands">Hands of that format played during the week (15+ BB).</param>
public sealed record WeekResponse(DateOnly WeekStart, TableFormat Format, int ReferenceVersion, DateTimeOffset CreatedAt, IReadOnlyList<PriorityResponse> Priorities, int Hands);

/// <param name="Current">Null until there is enough recent history (minBaselineHands) to build a plan.</param>
/// <param name="BaselineHands">Recent hands available when no plan could be built yet.</param>
public sealed record ProgressResponse(
    DateOnly WeekStart,
    WeekResponse? Current,
    int BaselineHands,
    int MinBaselineHands,
    int MinWeekSpots,
    int DrillAttempts,
    int DrillCorrect,
    int DrillGoal,
    IReadOnlyList<WeekResponse> History);

/// <summary>The weekly plan (roadmap step 4).</summary>
public static class ProgressEndpoints
{
    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/progress").WithTags("Progress");
        // GET builds this week's plan the first time it is read: idempotent (one plan per week, enforced
        // by the key), so it stays a safe, cacheable-in-spirit read for the client.
        group.MapGet("", GetAsync);
        group.MapPost("/plan/rebuild", RebuildAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        return endpoints;
    }

    private static async Task<Results<Ok<ProgressResponse>, UnauthorizedHttpResult>> GetAsync(
        HttpContext context,
        ProgressService progress,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(ToResponse(await progress.GetAsync(userId, cancellationToken)));
    }

    private static async Task<Results<Ok<ProgressResponse>, UnauthorizedHttpResult>> RebuildAsync(
        HttpContext context,
        ProgressService progress,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(ToResponse(await progress.RebuildAsync(userId, cancellationToken)));
    }

    private static ProgressResponse ToResponse(ProgressReport r) => new(
        r.WeekStart,
        r.Current is null ? null : Week(r.Current),
        r.BaselineHands,
        ProgressService.MinBaselineHands,
        WeeklyPlanner.MinWeekSpots,
        r.DrillAttempts,
        r.DrillCorrect,
        r.DrillGoal,
        r.History.Select(Week).ToList());

    private static WeekResponse Week(WeekReport w) => new(
        w.Plan.WeekStart,
        w.Plan.Format,
        w.Plan.ReferenceVersion,
        w.Plan.CreatedAt,
        w.Priorities.Select(p => new PriorityResponse(
            p.Priority.Stat,
            p.Priority.Position,
            p.Priority.Direction,
            p.Priority.Confidence,
            p.Priority.BaselineRate,
            p.Priority.BaselineOpportunities,
            p.Priority.Min,
            p.Priority.Max,
            p.Week.Made,
            p.Week.Opportunities,
            p.Week.Rate,
            p.Week.Status,
            p.Drill is null ? null : new PriorityDrillResponse(p.Drill.Defence, p.Drill.Positions))).ToList(),
        w.Hands);
}
