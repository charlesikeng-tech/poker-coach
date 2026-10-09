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

public sealed record TournamentResponse(
    Guid Id,
    string Name,
    DateTimeOffset? StartedAt,
    string? Currency,
    decimal? BuyIn,
    int? RegisteredPlayers,
    int? FinishPosition,
    int HandCount,
    TournamentResultResponse Result);

/// <param name="Roi">Ratio: 0.12 means +12 %.</param>
public sealed record TournamentTotalsResponse(
    int Tournaments,
    int TournamentsWithResult,
    int Entries,
    decimal BuyIns,
    decimal Winnings,
    decimal Profit,
    decimal? Roi);

public sealed record TournamentPageResponse(
    IReadOnlyList<TournamentResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    TournamentTotalsResponse Totals);

public static class TournamentEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTournamentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/tournaments").WithTags("Tournaments").MapGet("/", ListAsync);
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

        var totals = result.Totals;
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
                    i.Result.Profit))).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            new TournamentTotalsResponse(
                totals.Tournaments,
                totals.TournamentsWithResult,
                totals.Entries,
                totals.BuyIns,
                totals.Winnings,
                totals.Profit,
                totals.Roi)));
    }
}
