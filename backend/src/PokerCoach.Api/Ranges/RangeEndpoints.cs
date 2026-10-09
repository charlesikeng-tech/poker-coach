using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Leaks;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Ranges;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Api.Ranges;

/// <param name="Hand">Starting hand: "AA", "AKs", "AKo".</param>
/// <param name="Dealt">Times dealt when folded to (raise-first-in spot).</param>
public sealed record RangeCellResponse(string Hand, int Dealt, int Opens, int Limps);

/// <param name="Reference">The position's reference opening rate (ratios), when one exists.</param>
/// <param name="Cells">The 169 starting hands in grid order: row by row from aces, pairs on the diagonal,
/// suited above, offsuit below.</param>
public sealed record PositionRangeResponse(
    PokerPosition Position,
    int Dealt,
    int Opens,
    int Limps,
    StatRateResponse OpenRate,
    ReferenceRangeResponse? Reference,
    IReadOnlyList<RangeCellResponse> Cells);

/// <param name="PendingHands">Hands still being analysed: ranges are partial while above zero.</param>
public sealed record OpeningRangesResponse(IReadOnlyList<PositionRangeResponse> Positions, int PendingHands, int ReferenceVersion);

public static class RangeEndpoints
{
    public static IEndpointRouteBuilder MapRangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/ranges/opening", GetOpeningAsync).WithTags("Ranges");
        return endpoints;
    }

    /// <summary>The player's actual opening ranges by position (ADR-0009): counts from his own hands.</summary>
    /// <param name="to">Exclusive upper bound on the hand start time.</param>
    /// <param name="maxStackBb">Exclusive upper bound on the hero's stack in big blinds.</param>
    private static async Task<Results<Ok<OpeningRangesResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetOpeningAsync(
        HttpContext context,
        RangeService ranges,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        decimal? minStackBb = null,
        decimal? maxStackBb = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        if (minStackBb is not null && maxStackBb is not null && minStackBb >= maxStackBb)
        {
            return ApiProblems.Validation("maxStackBb", "Must be above 'minStackBb'.");
        }

        var result = await ranges.GetOpeningAsync(userId, new StatisticsFilter(from, to, minStackBb, maxStackBb), cancellationToken);
        return TypedResults.Ok(new OpeningRangesResponse(
            result.Positions.Select(p => new PositionRangeResponse(
                p.Position,
                p.Dealt,
                p.Opens,
                p.Limps,
                new StatRateResponse(p.OpenRate.Made, p.OpenRate.Opportunities, p.OpenRate.Rate),
                p.Reference is { } r ? new ReferenceRangeResponse(r.Min, r.Max) : null,
                p.Cells.Select(c => new RangeCellResponse(c.Hand.ToString(), c.Dealt, c.Opens, c.Limps)).ToList())).ToList(),
            result.PendingHands,
            result.ReferenceVersion));
    }
}
