using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
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
    TournamentCoverageResponse? Coverage)
{
    public static TournamentResponse From(TournamentListItem i) => new(
        i.Id,
        i.Name,
        i.StartedAt,
        i.Currency,
        i.BuyIn,
        i.RegisteredPlayers,
        i.FinishPosition,
        i.HandCount,
        new TournamentResultResponse(i.Result.Status, i.Result.Entries, i.Result.TotalBuyIn, i.Result.PrizeWinnings, i.Result.BountyWinnings, i.Result.Profit),
        TournamentCoverageResponse.From(i.Coverage));
}

/// <param name="Index">Position of the hand in the tournament, from 1.</param>
/// <param name="Stack">Hero's chips at the start of the hand.</param>
public sealed record StackPointResponse(int Index, DateTimeOffset StartedAt, int Level, long Stack, decimal StackInBigBlinds);

/// <param name="StackShare">Net chips over the starting stack, as a ratio: 1 is a double-up, −1 a bust.</param>
public sealed record KeyMomentResponse(
    int Index,
    Guid HandId,
    DateTimeOffset StartedAt,
    int Level,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    long NetChips,
    decimal NetBigBlinds,
    decimal StackShare,
    KeyMomentStage Stage);

/// <param name="HandsDurationMinutes">From the first to the last imported hand; null with fewer than two hands.</param>
/// <param name="Stats">Over this tournament's hands only: descriptive, too few hands to judge a leak.</param>
/// <param name="PendingHands">Hands whose facts are still being computed: stats and key moments are partial until 0.</param>
public sealed record TournamentDetailResponse(
    TournamentResponse Tournament,
    string? Type,
    string? Speed,
    string? Mode,
    int? HandsDurationMinutes,
    IReadOnlyList<StackPointResponse> Stack,
    IReadOnlyList<KeyMomentResponse> KeyMoments,
    StatLineResponse Stats,
    int PendingHands);

/// <summary>Money and rates cover tournaments with a known result only.</summary>
/// <param name="Roi">Ratio: 0.12 means +12 %.</param>
/// <param name="ItmRate">Share of entries that won a prize (bounties excluded), as a ratio.</param>
/// <param name="PrizeWinnings">Sum of prize winnings across known results.</param>
/// <param name="BountyWinnings">Sum of bounty winnings across known results.</param>
/// <param name="Fees">Sum of fees; null when the breakdown is unavailable for some results.</param>
/// <param name="BountyBuyIns">Sum of bounty buy-ins (0 when the player only plays non-KO).</param>
/// <param name="PrizePoolBuyIns">Sum of prize-pool buy-ins; null when the breakdown is unavailable for some results.</param>
/// <param name="RebuysCost">Sum of rebuy and add-on costs.</param>
public sealed record PerformanceFiguresResponse(
    int Tournaments,
    int TournamentsWithResult,
    int Entries,
    int PaidEntries,
    decimal BuyIns,
    decimal Winnings,
    decimal Profit,
    decimal? Roi,
    decimal? RoiLow,
    decimal? RoiHigh,
    decimal? ItmRate,
    decimal PrizeWinnings,
    decimal BountyWinnings,
    decimal? Fees,
    decimal BountyBuyIns,
    decimal? PrizePoolBuyIns,
    decimal RebuysCost)
{
    public static PerformanceFiguresResponse From(PerformanceFigures f) =>
        new(f.Tournaments, f.TournamentsWithResult, f.Entries, f.PaidEntries, f.BuyIns, f.Winnings, f.Profit, f.Roi, f.RoiLow, f.RoiHigh, f.ItmRate,
            f.PrizeWinnings, f.BountyWinnings, f.Fees, f.BountyBuyIns, f.PrizePoolBuyIns, f.RebuysCost);
}

public sealed record TournamentPageResponse(
    IReadOnlyList<TournamentResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    PerformanceFiguresResponse Totals);

public sealed record ProfitPointResponse(int Index, DateTimeOffset? StartedAt, string Name, decimal Profit, decimal CumulativeProfit);

/// <param name="Key">Band name (upTo5, from5To10, from10To20, from20To50, over50) or the room's raw value; null when unknown.</param>
public sealed record PerformanceGroupResponse(string? Key, PerformanceFiguresResponse Figures);

public sealed record PerformanceResponse(
    PerformanceFiguresResponse Totals,
    IReadOnlyList<ProfitPointResponse> Curve,
    IReadOnlyList<PerformanceGroupResponse> ByBuyIn,
    IReadOnlyList<PerformanceGroupResponse> ByType,
    IReadOnlyList<PerformanceGroupResponse> BySpeed,
    IReadOnlyList<PerformanceGroupResponse> ByMonth,
    IReadOnlyList<PerformanceGroupResponse> ByDayOfWeek,
    IReadOnlyList<PerformanceGroupResponse> ByTimeSlot,
    IReadOnlyList<PokerRoom> Rooms);

public static class TournamentEndpoints
{
    private const int MaxPageSize = 100;
    private const int MaxSearchLength = 100;

    public static IEndpointRouteBuilder MapTournamentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tournaments = endpoints.MapGroup("/api/tournaments").WithTags("Tournaments");
        tournaments.MapGet("/", ListAsync);
        tournaments.MapGet("/{id:guid}", GetAsync);
        endpoints.MapGet("/api/performance", GetPerformanceAsync).WithTags("Performance");
        return endpoints;
    }

    /// <param name="to">Exclusive upper bound on the start time.</param>
    /// <param name="search">Part of the tournament name, case-insensitive.</param>
    private static async Task<Results<Ok<TournamentPageResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(
        HttpContext context,
        PlanAccess plans,
        TournamentListService tournaments,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        decimal? minBuyIn = null,
        decimal? maxBuyIn = null,
        int page = 1,
        int pageSize = 50,
        string? search = null,
        string? mode = null,
        string? status = null)
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

        if (search is { Length: > MaxSearchLength })
        {
            return ApiProblems.Validation("search", $"At most {MaxSearchLength} characters.");
        }

        // Free plan: history window (ADR-0014). Data is kept, only not shown.
        from = await plans.ClampFromAsync(userId, from, cancellationToken);
        var result = await tournaments.ListAsync(
            userId,
            new TournamentFilter(from, to, minBuyIn, maxBuyIn, page, pageSize, search, mode, status),
            cancellationToken);

        return TypedResults.Ok(new TournamentPageResponse(
            result.Items.Select(TournamentResponse.From).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            PerformanceFiguresResponse.From(result.Totals)));
    }

    /// <summary>One tournament: result, stack over time, the hands that decided it, session stats.</summary>
    private static async Task<Results<Ok<TournamentDetailResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        Guid id,
        HttpContext context,
        TournamentDetailService details,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        // Someone else's tournament answers like a missing one: ids are not an oracle.
        var detail = await details.GetAsync(userId, id, cancellationToken);
        if (detail is null)
        {
            return ApiProblems.WithCode(StatusCodes.Status404NotFound, "TOURNAMENT_NOT_FOUND", "No such tournament.");
        }

        return TypedResults.Ok(new TournamentDetailResponse(
            TournamentResponse.From(detail.Tournament),
            detail.Type,
            detail.Speed,
            detail.Mode,
            detail.HandsDuration is { } duration ? (int)Math.Round(duration.TotalMinutes) : null,
            detail.Stack.Select(p => new StackPointResponse(p.Index, p.StartedAt, p.Level, p.Stack, p.StackInBigBlinds)).ToList(),
            detail.KeyMoments.Select(k => new KeyMomentResponse(
                k.Moment.Index,
                k.HandId,
                k.StartedAt,
                k.Level,
                k.Position,
                k.HeroCards,
                k.StackInBigBlinds,
                k.Moment.NetChips,
                k.Moment.NetBigBlinds,
                k.Moment.StackShare,
                k.Moment.Stage)).ToList(),
            StatLineResponse.From(detail.Stats),
            detail.PendingHands));
    }

    /// <param name="to">Exclusive upper bound on the start time.</param>
    /// <param name="timeZone">IANA time zone of the player ("Europe/Paris") for days and time slots; omitted: UTC.</param>
    private static async Task<Results<Ok<PerformanceResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetPerformanceAsync(
        HttpContext context,
        PlanAccess plans,
        PerformanceService performance,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? timeZone = null,
        string? room = null)
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

        if (!PokerRoomQuery.TryParse(room, out var parsedRoom))
        {
            return ApiProblems.Validation("room", "Must be a valid room name (winamax, pokerStars, betclic, pmu).");
        }

        // Free plan: history window (ADR-0014). Data is kept, only not shown.
        from = await plans.ClampFromAsync(userId, from, cancellationToken);
        var report = await performance.GetAsync(userId, from, to, cancellationToken, zone, parsedRoom);
        return TypedResults.Ok(new PerformanceResponse(
            PerformanceFiguresResponse.From(report.Totals),
            report.Curve.Select(p => new ProfitPointResponse(p.Index, p.StartedAt, p.Name, p.Profit, p.CumulativeProfit)).ToList(),
            Groups(report.ByBuyIn),
            Groups(report.ByType),
            Groups(report.BySpeed),
            Groups(report.ByMonth),
            Groups(report.ByDayOfWeek),
            Groups(report.ByTimeSlot),
            report.Rooms));
    }

    private static List<PerformanceGroupResponse> Groups(IReadOnlyList<PerformanceGroup> groups) =>
        groups.Select(g => new PerformanceGroupResponse(g.Key, PerformanceFiguresResponse.From(g.Figures))).ToList();
}
