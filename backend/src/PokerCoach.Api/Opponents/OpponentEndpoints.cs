using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Coaching;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Opponents;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Api.Opponents;

public sealed record OpponentListItemResponse(
    Guid Id,
    string OpponentName,
    PokerRoom Room,
    string ScreenName,
    int Hands,
    PlayerType Type,
    decimal VpipRate,
    decimal PfrRate,
    decimal ThreeBetRate,
    string? Note,
    IReadOnlyList<string> Tags,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public sealed record OpponentListResponse(IReadOnlyList<OpponentListItemResponse> Items, int TotalCount);

public sealed record OpponentStatsResponse(
    int Hands,
    decimal VpipRate,
    decimal PfrRate,
    decimal? RfiRate,
    decimal? StealRate,
    decimal ThreeBetRate,
    decimal? FoldToThreeBetRate,
    decimal? CbetFlopRate,
    decimal? FoldToCbetFlopRate,
    decimal AggressionFrequency,
    decimal WtsdRate,
    decimal? WsdRate);

public sealed record OpponentDetailResponse(
    Guid Id,
    string OpponentName,
    PokerRoom Room,
    string ScreenName,
    OpponentStatsResponse Stats,
    PlayerType Type,
    string? Note,
    IReadOnlyList<string> Tags,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int DistinctTournaments);

public sealed record SetNoteRequest(string? Text);

public sealed record SetTagsRequest(IReadOnlyList<string> Tags);

public sealed record ExploitTipResponse(string Key, string Category);

public sealed record ExploitResponse(
    PlayerType PlayerType,
    string ValueBetSizing,
    string BluffFrequency,
    IReadOnlyList<ExploitTipResponse> Tips);

public sealed record OpponentAdjustmentResponse(string Spot, string Action, string Why);

public sealed record OpponentCoachingResponse(
    string Summary,
    IReadOnlyList<OpponentAdjustmentResponse> Adjustments,
    IReadOnlyList<string> KeyTips,
    bool Stale,
    string Model,
    DateTimeOffset CreatedAt);

public sealed record GenerateOpponentCoachingRequest(string? Language);

public static class OpponentEndpoints
{
    public static IEndpointRouteBuilder MapOpponentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/opponents").WithTags("Opponents");

        group.MapGet("/", ListAsync);
        group.MapGet("/{opponentId:guid}", GetAsync);
        group.MapPut("/{opponentId:guid}/note", SetNoteAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapDelete("/{opponentId:guid}/note", ClearNoteAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPut("/{opponentId:guid}/tags", SetTagsAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapGet("/{opponentId:guid}/exploit", GetExploitAsync);
        group.MapGet("/{opponentId:guid}/coaching", GetCoachingAsync);
        group.MapPost("/{opponentId:guid}/coaching", GenerateCoachingAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

        return endpoints;
    }

    private static async Task<Results<Ok<OpponentListResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(
        HttpContext context,
        OpponentService opponents,
        CancellationToken cancellationToken,
        string? search = null,
        string? sort = null,
        bool desc = true,
        int page = 1,
        int pageSize = 50)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        if (page < 1)
            return ApiProblems.Validation("page", "Must be at least 1.");
        if (pageSize is < 1 or > 200)
            return ApiProblems.Validation("pageSize", "Must be between 1 and 200.");

        if (!TryParseSort(sort, out var parsedSort))
            return ApiProblems.Validation("sort", "Must be hands, name, lastSeen or vpip.");

        var result = await opponents.ListAsync(userId, search, parsedSort, desc, page, pageSize, cancellationToken);
        return TypedResults.Ok(new OpponentListResponse(
            result.Items.Select(ToResponse).ToList(),
            result.TotalCount));
    }

    private static async Task<Results<Ok<OpponentDetailResponse>, NotFound, UnauthorizedHttpResult>> GetAsync(
        Guid opponentId,
        HttpContext context,
        OpponentService opponents,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        var detail = await opponents.GetAsync(userId, opponentId, cancellationToken);
        if (detail is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(ToDetailResponse(detail));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetNoteAsync(
        Guid opponentId,
        SetNoteRequest request,
        HttpContext context,
        OpponentService opponents,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        if (request.Text is { Length: > 4000 })
            return ApiProblems.Validation("text", "Must be at most 4000 characters.");

        return await opponents.SetNoteAsync(userId, opponentId, request.Text, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> ClearNoteAsync(
        Guid opponentId,
        HttpContext context,
        OpponentService opponents,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        return await opponents.SetNoteAsync(userId, opponentId, null, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetTagsAsync(
        Guid opponentId,
        SetTagsRequest request,
        HttpContext context,
        OpponentService opponents,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        if (request.Tags.Count > 10)
            return ApiProblems.Validation("tags", "At most 10 tags.");

        return await opponents.SetTagsAsync(userId, opponentId, request.Tags, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ExploitResponse>, NotFound, UnauthorizedHttpResult>> GetExploitAsync(
        Guid opponentId,
        HttpContext context,
        OpponentService opponents,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        var detail = await opponents.GetAsync(userId, opponentId, cancellationToken);
        if (detail is null)
            return TypedResults.NotFound();

        var exploit = ExploitAdjustment.For(detail.Type);
        return TypedResults.Ok(new ExploitResponse(
            exploit.PlayerType,
            exploit.ValueBetSizing.ToString(),
            exploit.BluffFrequency.ToString(),
            exploit.Tips.Select(t => new ExploitTipResponse(t.Key, t.Category.ToString())).ToList()));
    }

    private static async Task<Results<Ok<OpponentCoachingResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetCoachingAsync(
        Guid opponentId,
        string? language,
        HttpContext context,
        OpponentCoachService coaching,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        var (lang, _, problem) = await CoachingEndpoints.LanguageAsync(language, userId, profiles, cancellationToken);
        if (problem is not null) return problem;

        var view = await coaching.GetAsync(userId, opponentId, lang!, cancellationToken);
        return view is null
            ? ApiProblems.WithCode(StatusCodes.Status404NotFound, "OPPONENT_COACHING_NOT_FOUND", "No coaching report written yet.")
            : TypedResults.Ok(ToCoachingResponse(view));
    }

    private static async Task<Results<Ok<OpponentCoachingResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GenerateCoachingAsync(
        Guid opponentId,
        GenerateOpponentCoachingRequest? request,
        HttpContext context,
        OpponentCoachService coaching,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
            return TypedResults.Unauthorized();

        var (lang, _, problem) = await CoachingEndpoints.LanguageAsync(request?.Language, userId, profiles, cancellationToken);
        if (problem is not null) return problem;

        var outcome = await coaching.GenerateAsync(userId, opponentId, lang!, cancellationToken);
        return outcome.View is { } view
            ? TypedResults.Ok(ToCoachingResponse(view))
            : CoachingEndpoints.Problem(outcome.Failure!.Value, "OPPONENT_NOT_FOUND");
    }

    private static OpponentCoachingResponse ToCoachingResponse(ReportView<OpponentCoachingAdvice> view)
    {
        var a = view.Report;
        return new OpponentCoachingResponse(
            a.Summary,
            a.Adjustments.Select(adj => new OpponentAdjustmentResponse(adj.Spot, adj.Action, adj.Why)).ToList(),
            a.KeyTips.ToList(),
            view.Stale, view.Model, view.CreatedAt);
    }

    private static OpponentListItemResponse ToResponse(OpponentListItem item) =>
        new(item.Id, item.OpponentName, item.Room, item.ScreenName,
            item.Hands, item.Type, item.VpipRate, item.PfrRate, item.ThreeBetRate,
            item.Note, item.Tags, item.FirstSeenAt, item.LastSeenAt);

    private static OpponentDetailResponse ToDetailResponse(OpponentDetail d) =>
        new(d.Id, d.OpponentName, d.Room, d.ScreenName,
            new OpponentStatsResponse(
                d.Stats.Hands,
                d.Stats.VpipRate, d.Stats.PfrRate,
                d.Stats.RfiRate, d.Stats.StealRate,
                d.Stats.ThreeBetRate, d.Stats.FoldToThreeBetRate,
                d.Stats.CbetFlopRate, d.Stats.FoldToCbetFlopRate,
                d.Stats.AggressionFrequency, d.Stats.WtsdRate, d.Stats.WsdRate),
            d.Type, d.Note, d.Tags, d.FirstSeenAt, d.LastSeenAt, d.DistinctTournaments);

    private static bool TryParseSort(string? value, out OpponentSort sort)
    {
        sort = OpponentSort.Hands;
        if (string.IsNullOrEmpty(value)) return true;
        return Enum.TryParse(value, ignoreCase: true, out sort) && Enum.IsDefined(sort) && !int.TryParse(value, out _);
    }
}
