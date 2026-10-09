using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Leaks;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Ranges;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Api.Ranges;

/// <param name="Hand">Starting hand: "AA", "AKs", "AKo".</param>
/// <param name="Dealt">Times dealt when folded to (raise-first-in spot).</param>
/// <param name="InReference">The reference range raises it first in.</param>
public sealed record RangeCellResponse(string Hand, int Dealt, int Opens, int Limps, bool InReference);

/// <param name="Position">Seat by distance to the button within the format: "utg" is three before it at
/// 6-max, six before it at full ring.</param>
/// <param name="ReferenceRate">The position's reference opening rate (ratios), when one exists.</param>
/// <param name="ReferenceNotation">The reference range as written ("22+, A2s+, …").</param>
/// <param name="ReferenceShare">Share of all two-card holdings the reference opens (ratio).</param>
/// <param name="Cells">The 169 starting hands in grid order: row by row from aces, pairs on the diagonal,
/// suited above, offsuit below.</param>
public sealed record PositionRangeResponse(
    PokerPosition Position,
    int Dealt,
    int Opens,
    int Limps,
    StatRateResponse OpenRate,
    ReferenceRangeResponse? ReferenceRate,
    string? ReferenceNotation,
    decimal? ReferenceShare,
    IReadOnlyList<RangeCellResponse> Cells);

/// <param name="PendingHands">Hands still being analysed: ranges are partial while above zero.</param>
public sealed record OpeningRangesResponse(
    TableFormat Format,
    StackBand Band,
    FormatCountsResponse Spots,
    IReadOnlyList<PositionRangeResponse> Positions,
    int PendingHands,
    int ReferenceVersion);

public static class RangeEndpoints
{
    public static IEndpointRouteBuilder MapRangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/ranges/opening", GetOpeningAsync).WithTags("Ranges");
        return endpoints;
    }

    /// <summary>
    /// The player's actual opening ranges by position next to the reference ones (ADR-0009), for one stack
    /// band: counts from his own hands, reference version 1.
    /// </summary>
    /// <param name="format">sixMax or fullRing; omitted: the format with the most spots.</param>
    /// <param name="band">short (15–25 BB), mid (25–40 BB) or deep (40 BB and more).</param>
    /// <param name="to">Exclusive upper bound on the hand start time.</param>
    private static async Task<Results<Ok<OpeningRangesResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetOpeningAsync(
        HttpContext context,
        RangeService ranges,
        CancellationToken cancellationToken,
        string? format = null,
        string band = "mid",
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!Enum.TryParse<StackBand>(band, ignoreCase: true, out var stackBand) || !Enum.IsDefined(stackBand))
        {
            return ApiProblems.Validation("band", "Must be one of: short, mid, deep.");
        }

        if (!TableFormatQuery.TryParse(format, out var tableFormat))
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        var result = await ranges.GetOpeningAsync(userId, tableFormat, stackBand, from, to, cancellationToken);
        return TypedResults.Ok(new OpeningRangesResponse(
            result.Format,
            result.Band,
            new FormatCountsResponse(result.SpotsByFormat[TableFormat.SixMax], result.SpotsByFormat[TableFormat.FullRing]),
            result.Positions.Select(p => new PositionRangeResponse(
                p.Position,
                p.Dealt,
                p.Opens,
                p.Limps,
                new StatRateResponse(p.OpenRate.Made, p.OpenRate.Opportunities, p.OpenRate.Rate),
                p.ReferenceRate is { } r ? new ReferenceRangeResponse(r.Min, r.Max) : null,
                p.ReferenceNotation,
                p.ReferenceShare,
                p.Cells.Select(c => new RangeCellResponse(c.Hand.ToString(), c.Dealt, c.Opens, c.Limps, c.InReference)).ToList())).ToList(),
            result.PendingHands,
            result.ReferenceVersion));
    }
}
