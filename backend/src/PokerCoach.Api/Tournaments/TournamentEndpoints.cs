using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Api.Tournaments;

/// <summary>Money amounts are null unless <c>result.status</c> is <c>known</c>: unknown is never sent as zero.</summary>
public sealed record TournamentResultResponse(
    TournamentResultStatus Status,
    int? Entries,
    decimal? TotalBuyIn,
    decimal? PrizeWinnings,
    decimal? BountyWinnings,
    decimal? Profit);

/// <summary>How much of the tournament the imported hands cover. See TournamentCoverage.</summary>
public sealed record TournamentCoverageResponse(
    CoverageStatus Status,
    int HandCount,
    int? FirstLevel,
    int? LastLevel,
    int MissingHands,
    int StackBreaks,
    int EntriesSeen,
    bool? StartMissing,
    bool EndSeen)
{
    public static TournamentCoverageResponse? From(TournamentCoverage? c) => c is null
        ? null
        : new(c.Status, c.HandCount, c.FirstLevel, c.LastLevel, c.MissingHands, c.StackBreaks, c.EntriesSeen, c.StartMissing, c.EndSeen);
}

/// <param name="Coverage">Null while it is being computed.</param>
public sealed record TournamentResponse(
    Guid Id,
    string Name,
    DateTimeOffset? StartedAt,
    string? Currency,
    decimal? BuyIn,
    int? RegisteredPlayers,
    int? FinishPosition,
    int HandCount,
    TournamentResultResponse Result,
    TournamentCoverageResponse? Coverage);

/// <summary>Money and rates cover tournaments with a known result only.</summary>
/// <param name="Roi">Ratio: 0.12 means +12 %.</param>
/// <param name="ItmRate">Share of entries that won a prize (bounties excluded), as a ratio.</param>
public sealed record PerformanceFiguresResponse(
    int Tournaments,
    int TournamentsWithResult,
    int Entries,
    int PaidEntries,
    decimal BuyIns,
    decimal Winnings,
    decimal Profit,
    decimal? Roi,
    decimal? ItmRate)
{
    public static PerformanceFiguresResponse From(PerformanceFigures f) =>
        new(f.Tournaments, f.TournamentsWithResult, f.Entries, f.PaidEntries, f.BuyIns, f.Winnings, f.Profit, f.Roi, f.ItmRate);
}

public sealed record TournamentPageResponse(
    IReadOnlyList<TournamentResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    PerformanceFiguresResponse Totals);

public sealed record ProfitPointResponse(int Index, DateTimeOffset? StartedAt, string Name, decimal Profit, decimal CumulativeProfit);

/// <param name="Key">Band name (upTo5, from5To10, from10To20, over20) or the room's raw value; null when unknown.</param>
public sealed record PerformanceGroupResponse(string? Key, PerformanceFiguresResponse Figures);

public sealed record PerformanceResponse(
    PerformanceFiguresResponse Totals,
    IReadOnlyList<ProfitPointResponse> Curve,
    IReadOnlyList<PerformanceGroupResponse> ByBuyIn,
    IReadOnlyList<PerformanceGroupResponse> ByType,
    IReadOnlyList<PerformanceGroupResponse> BySpeed);

public static class TournamentEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTournamentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/tournaments").WithTags("Tournaments").MapGet("/", ListAsync);
        endpoints.MapGet("/api/performance", GetPerformanceAsync).WithTags("Performance");
        return endpoints;
    }

    /// <param name="to">Exclusive upper bound on the start time.</param>
    private static async Task<Results<Ok<TournamentPageResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(
        HttpContext context,
        TournamentListService tournaments,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        decimal? minBuyIn = null,
        decimal? maxBuyIn = null,
        int page = 1,
        int pageSize = 50)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (page < 1)
        {
            return ApiProblems.Validation("page", "Must be 1 or more.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return ApiProblems.Validation("pageSize", $"Must be between 1 and {MaxPageSize}.");
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        if (minBuyIn is not null && maxBuyIn is not null && minBuyIn > maxBuyIn)
        {
            return ApiProblems.Validation("maxBuyIn", "Must not be below 'minBuyIn'.");
        }

        var result = await tournaments.ListAsync(
            userId,
            new TournamentFilter(from, to, minBuyIn, maxBuyIn, page, pageSize),
            cancellationToken);

        return TypedResults.Ok(new TournamentPageResponse(
            result.Items.Select(i => new TournamentResponse(
                i.Id,
                i.Name,
                i.StartedAt,
                i.Currency,
                i.BuyIn,
                i.RegisteredPlayers,
                i.FinishPosition,
                i.HandCount,
                new TournamentResultResponse(
                    i.Result.Status,
                    i.Result.Entries,
                    i.Result.TotalBuyIn,
                    i.Result.PrizeWinnings,
                    i.Result.BountyWinnings,
                    i.Result.Profit),
                TournamentCoverageResponse.From(i.Coverage))).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            PerformanceFiguresResponse.From(result.Totals)));
    }

    /// <param name="to">Exclusive upper bound on the start time.</param>
    private static async Task<Results<Ok<PerformanceResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetPerformanceAsync(
        HttpContext context,
        PerformanceService performance,
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

        var report = await performance.GetAsync(userId, from, to, cancellationToken);
        return TypedResults.Ok(new PerformanceResponse(
            PerformanceFiguresResponse.From(report.Totals),
            report.Curve.Select(p => new ProfitPointResponse(p.Index, p.StartedAt, p.Name, p.Profit, p.CumulativeProfit)).ToList(),
            Groups(report.ByBuyIn),
            Groups(report.ByType),
            Groups(report.BySpeed)));
    }

    private static List<PerformanceGroupResponse> Groups(IReadOnlyList<PerformanceGroup> groups) =>
        groups.Select(g => new PerformanceGroupResponse(g.Key, PerformanceFiguresResponse.From(g.Figures))).ToList();
}
