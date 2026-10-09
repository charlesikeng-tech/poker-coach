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

public sealed record LuckPointResponse(int Index, DateTimeOffset StartedAt, decimal ActualBigBlinds, decimal ExpectedBigBlinds);

/// <param name="Equity">The hero's share of the main pot when the money went in (0–1).</param>
/// <param name="Luck">Actual minus expected, in big blinds.</param>
public sealed record LuckSwingResponse(Guid HandId, Guid TournamentId, DateTimeOffset StartedAt, string? HeroCards, decimal Equity, decimal NetBigBlinds, decimal Luck);

/// <summary>Preflop all-ins with every hand shown: actual against expected, in big blinds.</summary>
/// <param name="ExpectedWins">Sum of the hero's equities: all-ins he "should" have won.</param>
/// <param name="Luck">Actual minus expected: positive when the board was kind.</param>
/// <param name="PendingHands">Hands whose facts are still being computed: figures will move.</param>
public sealed record AllInLuckResponse(
    int AllIns,
    int Won,
    decimal ExpectedWins,
    decimal? AverageEquity,
    decimal ActualBigBlinds,
    decimal ExpectedBigBlinds,
    decimal Luck,
    IReadOnlyList<LuckPointResponse> Curve,
    IReadOnlyList<LuckSwingResponse> Swings,
    int PendingHands);

public static class StatisticsEndpoints
{
    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/statistics", GetAsync).WithTags("Statistics");
        endpoints.MapGet("/api/statistics/all-in", GetAllInLuckAsync).WithTags("Statistics");
        return endpoints;
    }

    /// <summary>Luck at the all-ins. Every table format together: luck does not depend on the table size.</summary>
    /// <param name="to">Exclusive upper bound on the hand start time.</param>
    /// <param name="completeOnly">Only tournaments with a complete hand history.</param>
    private static async Task<Results<Ok<AllInLuckResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetAllInLuckAsync(
        HttpContext context,
        AllInLuckService luck,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        bool completeOnly = false)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        var report = await luck.GetAsync(userId, new StatisticsFilter(from, to, null, null, completeOnly), cancellationToken);
        return TypedResults.Ok(new AllInLuckResponse(
            report.AllIns,
            report.Won,
            report.ExpectedWins,
            report.AverageEquity,
            report.ActualBigBlinds,
            report.ExpectedBigBlinds,
            report.Luck,
            report.Curve.Select(p => new LuckPointResponse(p.Index, p.StartedAt, p.ActualBigBlinds, p.ExpectedBigBlinds)).ToList(),
            report.Swings.Select(s => new LuckSwingResponse(s.HandId, s.TournamentId, s.StartedAt, s.HeroCards, s.Equity, s.NetBigBlinds, s.Luck)).ToList(),
            report.PendingHands));
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
