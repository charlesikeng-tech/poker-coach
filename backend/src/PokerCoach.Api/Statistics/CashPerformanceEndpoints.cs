using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Billing;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Application.Statistics;

namespace PokerCoach.Api.Statistics;

public sealed record CashPerformanceFiguresResponse(int Hands, decimal NetBigBlinds, decimal? BigBlindsPer100, decimal NetMoney);

public sealed record CashPerformanceGroupResponse(string Key, CashPerformanceFiguresResponse Figures);

public sealed record CashProfitPointResponse(DateOnly Date, int Hands, decimal NetBigBlinds, decimal CumulativeNetBigBlinds, decimal NetMoney, decimal CumulativeNetMoney);

public sealed record CashPerformanceResponse(
    CashPerformanceFiguresResponse Totals,
    IReadOnlyList<CashProfitPointResponse> Curve,
    IReadOnlyList<CashPerformanceGroupResponse> ByStakes,
    IReadOnlyList<CashPerformanceGroupResponse> ByMonth,
    IReadOnlyList<CashPerformanceGroupResponse> ByDayOfWeek,
    IReadOnlyList<CashPerformanceGroupResponse> ByTimeSlot,
    int PendingHands);

public static class CashPerformanceEndpoints
{
    public static IEndpointRouteBuilder MapCashPerformanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/performance/cash", GetAsync).WithTags("Performance");
        return endpoints;
    }

    private static async Task<Results<Ok<CashPerformanceResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        HttpContext context,
        PlanAccess plans,
        CashPerformanceService performance,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? timeZone = null)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        TimeZoneInfo? zone = null;
        if (timeZone is not null && (timeZone.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out zone)))
        {
            return ApiProblems.Validation("timeZone", "Must be an IANA time zone, like Europe/Paris.");
        }

        from = await plans.ClampFromAsync(userId, from, cancellationToken);
        var report = await performance.GetAsync(userId, from, to, cancellationToken, zone);
        return TypedResults.Ok(new CashPerformanceResponse(
            new CashPerformanceFiguresResponse(report.Totals.Hands, report.Totals.NetBigBlinds, report.Totals.BigBlindsPer100, report.Totals.NetMoney),
            report.Curve.Select(p => new CashProfitPointResponse(p.Date, p.Hands, p.NetBigBlinds, p.CumulativeNetBigBlinds, p.NetMoney, p.CumulativeNetMoney)).ToList(),
            report.ByStakes.Select(g => ToGroup(g)).ToList(),
            report.ByMonth.Select(g => ToGroup(g)).ToList(),
            report.ByDayOfWeek.Select(g => ToGroup(g)).ToList(),
            report.ByTimeSlot.Select(g => ToGroup(g)).ToList(),
            report.PendingHands));
    }

    private static CashPerformanceGroupResponse ToGroup(CashPerformanceGroup g) =>
        new(g.Key, new CashPerformanceFiguresResponse(g.Figures.Hands, g.Figures.NetBigBlinds, g.Figures.BigBlindsPer100, g.Figures.NetMoney));
}
