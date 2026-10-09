using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Api.Statistics;

/// <param name="Rate">Ratio (0.243 = 24.3 %); null when there was no opportunity.</param>
public sealed record StatRateResponse(int Made, int Opportunities, decimal? Rate);

/// <param name="BigBlindsPer100">Big blinds won per 100 hands; null without hands.</param>
public sealed record StatLineResponse(
    int Hands,
    StatRateResponse Vpip,
    StatRateResponse Pfr,
    StatRateResponse Rfi,
    StatRateResponse Limp,
    StatRateResponse Steal,
    StatRateResponse ThreeBet,
    StatRateResponse FoldToThreeBet,
    StatRateResponse CbetFlop,
    StatRateResponse WentToShowdown,
    StatRateResponse WonAtShowdown,
    decimal? BigBlindsPer100)
{
    public static StatLineResponse From(StatLine l) => new(
        l.Hands,
        Rate(l.Vpip),
        Rate(l.Pfr),
        Rate(l.Rfi),
        Rate(l.Limp),
        Rate(l.Steal),
        Rate(l.ThreeBet),
        Rate(l.FoldToThreeBet),
        Rate(l.CbetFlop),
        Rate(l.WentToShowdown),
        Rate(l.WonAtShowdown),
        l.BigBlindsPer100);

    private static StatRateResponse Rate(StatRate r) => new(r.Made, r.Opportunities, r.Rate);
}

/// <param name="Position">Null when it could not be named.</param>
public sealed record PositionStatLineResponse(PokerPosition? Position, StatLineResponse Line);

/// <param name="Tournaments">Tournaments behind the filtered hands.</param>
/// <param name="CompleteTournaments">Of those, tournaments whose hand history is complete.</param>
public sealed record StatisticsSampleResponse(int Tournaments, int CompleteTournaments);

/// <param name="PendingHands">Hands still being analyzed: figures are partial while above zero.</param>
/// <param name="Format">Table format of the figures; positions are named within it.</param>
/// <param name="HandsByFormat">Hands per format with the other filters.</param>
public sealed record StatisticsResponse(
    StatLineResponse Overall,
    IReadOnlyList<PositionStatLineResponse> ByPosition,
    int PendingHands,
    StatisticsSampleResponse Sample,
    TableFormat Format,
    FormatCountsResponse HandsByFormat);

public static class StatisticsEndpoints
{
    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/statistics", GetAsync).WithTags("Statistics");
        return endpoints;
    }

    /// <param name="to">Exclusive upper bound on the hand start time.</param>
    /// <param name="maxStackBb">Exclusive upper bound on the hero's stack in big blinds.</param>
    /// <param name="completeOnly">Only tournaments with a complete hand history.</param>
    /// <param name="format">sixMax or fullRing; omitted: the format with the most hands.</param>
    private static async Task<Results<Ok<StatisticsResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        HttpContext context,
        StatisticsService statistics,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        decimal? minStackBb = null,
        decimal? maxStackBb = null,
        bool completeOnly = false,
        string? format = null)
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

        if (!TableFormatQuery.TryParse(format, out var tableFormat))
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        var report = await statistics.GetAsync(userId, new StatisticsFilter(from, to, minStackBb, maxStackBb, completeOnly, tableFormat), cancellationToken);
        return TypedResults.Ok(new StatisticsResponse(
            StatLineResponse.From(report.Overall),
            report.ByPosition.Select(p => new PositionStatLineResponse(p.Position, StatLineResponse.From(p.Line))).ToList(),
            report.PendingHands,
            new StatisticsSampleResponse(report.Sample.Tournaments, report.Sample.CompleteTournaments),
            report.Format,
            new FormatCountsResponse(report.HandsByFormat.SixMax, report.HandsByFormat.FullRing)));
    }
}
