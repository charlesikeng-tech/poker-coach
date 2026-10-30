using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Api.Statistics;

public sealed record CashSessionSummaryResponse(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationMinutes,
    int Hands,
    string Stakes,
    long BigBlind,
    decimal NetBigBlinds,
    decimal? BigBlindsPer100,
    decimal NetMoney,
    decimal? AllInAdjBigBlindsPer100,
    PokerRoom Room);

public sealed record CashSessionStackPointResponse(int Index, DateTimeOffset StartedAt, decimal StackInBigBlinds);

public sealed record CashSessionKeyMomentResponse(
    int Index,
    Guid HandId,
    DateTimeOffset StartedAt,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal NetBigBlinds);

/// <param name="PendingHands">Hands whose facts are still being computed: stats and key moments are partial until 0.</param>
public sealed record CashSessionDetailResponse(
    CashSessionSummaryResponse Summary,
    IReadOnlyList<CashSessionStackPointResponse> Stack,
    IReadOnlyList<CashSessionKeyMomentResponse> KeyMoments,
    StatLineResponse Stats,
    int PendingHands);

public sealed record CashSessionPageResponse(
    IReadOnlyList<CashSessionSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public static class CashSessionEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapCashSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sessions = endpoints.MapGroup("/api/cash-sessions").WithTags("CashSessions");
        sessions.MapGet("/", ListAsync);
        sessions.MapGet("/{id:guid}", GetAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<CashSessionPageResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(
        HttpContext context,
        PlanAccess plans,
        CashSessionService sessions,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
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

        from = await plans.ClampFromAsync(userId, from, cancellationToken);
        var result = await sessions.ListAsync(userId, from, to, page, pageSize, cancellationToken);
        return TypedResults.Ok(new CashSessionPageResponse(
            result.Items.Select(ToResponse).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    private static async Task<Results<Ok<CashSessionDetailResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        Guid id,
        HttpContext context,
        CashSessionService sessions,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var detail = await sessions.GetAsync(userId, id, cancellationToken);
        if (detail is null)
        {
            return ApiProblems.WithCode(StatusCodes.Status404NotFound, "SESSION_NOT_FOUND", "No such cash session.");
        }

        return TypedResults.Ok(new CashSessionDetailResponse(
            ToResponse(detail.Summary),
            detail.Stack.Select(p => new CashSessionStackPointResponse(p.Index, p.StartedAt, p.StackInBigBlinds)).ToList(),
            detail.KeyMoments.Select(k => new CashSessionKeyMomentResponse(k.Index, k.HandId, k.StartedAt, k.Position, k.HeroCards, k.StackInBigBlinds, k.NetBigBlinds)).ToList(),
            StatLineResponse.From(detail.Stats),
            detail.PendingHands));
    }

    private static CashSessionSummaryResponse ToResponse(CashSessionSummary s) =>
        new(s.Id, s.StartedAt, s.EndedAt, s.DurationMinutes, s.Hands, s.Stakes, s.BigBlind, s.NetBigBlinds, s.BigBlindsPer100, s.NetMoney, s.AllInAdjBigBlindsPer100, s.Room);
}
