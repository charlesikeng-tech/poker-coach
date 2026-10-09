using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Leaks;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;

namespace PokerCoach.Api.Leaks;

/// <param name="Min">Lower bound of the reference range (ratio).</param>
public sealed record ReferenceRangeResponse(decimal Min, decimal Max);

/// <param name="Rate">The hero's rate (ratio); IntervalLow/High bound it at 95 %.</param>
public sealed record LeakResponse(
    LeakStat Stat,
    PokerPosition? Position,
    LeakDirection Direction,
    LeakConfidence Confidence,
    decimal Rate,
    int Made,
    int Opportunities,
    decimal IntervalLow,
    decimal IntervalHigh,
    ReferenceRangeResponse Range);

public sealed record UnderSampledResponse(LeakStat Stat, PokerPosition? Position, int Opportunities, int Needed);

/// <param name="Hands">Hands looked at: only those with 15+ big blinds (push/fold play is judged differently).</param>
/// <param name="PendingHands">Hands still being analyzed.</param>
/// <param name="ReferenceVersion">Version of the reference ranges used.</param>
public sealed record LeakAnalysisResponse(
    IReadOnlyList<LeakResponse> Leaks,
    IReadOnlyList<UnderSampledResponse> UnderSampled,
    int Hands,
    int Tournaments,
    int CompleteTournaments,
    int PendingHands,
    int ReferenceVersion);

public static class LeakEndpoints
{
    public static IEndpointRouteBuilder MapLeakEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/leaks", GetAsync).WithTags("Leaks");
        return endpoints;
    }

    private static async Task<Results<Ok<LeakAnalysisResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        HttpContext context,
        LeakService leaks,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        var analysis = await leaks.GetAsync(userId, from, to, cancellationToken);
        return TypedResults.Ok(new LeakAnalysisResponse(
            analysis.Report.Leaks.Select(l => new LeakResponse(
                l.Stat,
                l.Position,
                l.Direction,
                l.Confidence,
                l.Rate,
                l.Made,
                l.Opportunities,
                l.IntervalLow,
                l.IntervalHigh,
                new ReferenceRangeResponse(l.Range.Min, l.Range.Max))).ToList(),
            analysis.Report.UnderSampled.Select(u => new UnderSampledResponse(u.Stat, u.Position, u.Opportunities, u.Needed)).ToList(),
            analysis.Hands,
            analysis.Sample.Tournaments,
            analysis.Sample.CompleteTournaments,
            analysis.PendingHands,
            analysis.ReferenceVersion));
    }
}
