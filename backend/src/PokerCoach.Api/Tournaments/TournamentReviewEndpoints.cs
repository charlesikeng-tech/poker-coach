using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Coaching;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Billing;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Hands;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Subscriptions;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Api.Tournaments;

public sealed record ReviewHandResponse(
    Guid HandId,
    int Index,
    int Level,
    PokerPosition? Position,
    string? HeroCards,
    decimal StackInBigBlinds,
    decimal NetBigBlinds,
    bool IsKeyMoment,
    bool SawFlop,
    HandCommentaryResponse? Commentary);

public sealed record HandCommentaryResponse(string Verdict, string Analysis, string? Suggestion);

public sealed record TournamentReviewResponse(
    Guid TournamentId,
    string TournamentName,
    bool HasDebrief,
    string? DebriefHeadline,
    string? DebriefStory,
    IReadOnlyList<string>? Strengths,
    IReadOnlyList<string>? WorkOn,
    IReadOnlyList<ReviewHandResponse> Hands,
    int CommentedCount,
    int TotalHands);

public sealed record GenerateReviewRequest(string? Language);

public sealed record CommentHandRequest(string? Language);

public static class TournamentReviewEndpoints
{
    public static IEndpointRouteBuilder MapTournamentReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tournaments/{tournamentId:guid}/review").WithTags("TournamentReview");
        group.MapGet("", GetReviewAsync);
        group.MapPost("", GenerateReviewAsync).RequiresFeature(Feature.HandCommentary);
        group.MapPost("hands/{handId:guid}", CommentHandAsync).RequiresFeature(Feature.HandCommentary);
        return endpoints;
    }

    private static async Task<Results<Ok<TournamentReviewResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GetReviewAsync(
        Guid tournamentId,
        string? language,
        HttpContext context,
        TournamentReviewService reviews,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        var (user, validationError) = await CoachingEndpoints.ResolveUserAsync(language, context, profiles, cancellationToken);
        if (validationError is not null) return validationError;
        if (user is not { } u) return TypedResults.Unauthorized();

        var review = await reviews.GetAsync(u.UserId, tournamentId, u.Language, cancellationToken);
        if (review is null)
            return ApiProblems.WithCode(StatusCodes.Status404NotFound, "TOURNAMENT_NOT_FOUND", "No such tournament.");

        return TypedResults.Ok(ToResponse(review));
    }

    private static async Task<Results<Ok<TournamentReviewResponse>, ProblemHttpResult, UnauthorizedHttpResult>> GenerateReviewAsync(
        Guid tournamentId,
        GenerateReviewRequest? request,
        HttpContext context,
        TournamentReviewService reviews,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        var (user, validationError) = await CoachingEndpoints.ResolveUserAsync(request?.Language, context, profiles, cancellationToken);
        if (validationError is not null) return validationError;
        if (user is not { } u) return TypedResults.Unauthorized();

        var outcome = await reviews.GenerateKeyMomentCommentaryAsync(u.UserId, tournamentId, u.Language, u.StakesLevel, cancellationToken);
        return outcome.View is { } view
            ? TypedResults.Ok(ToResponse(view.Report))
            : CoachingEndpoints.Problem(outcome.Failure!.Value, "REVIEW_NOT_AVAILABLE");
    }

    private static async Task<Results<Ok<HandCommentaryResponse>, ProblemHttpResult, UnauthorizedHttpResult>> CommentHandAsync(
        Guid tournamentId,
        Guid handId,
        CommentHandRequest? request,
        HttpContext context,
        TournamentReviewService reviews,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        var (user, validationError) = await CoachingEndpoints.ResolveUserAsync(request?.Language, context, profiles, cancellationToken);
        if (validationError is not null) return validationError;
        if (user is not { } u) return TypedResults.Unauthorized();

        var outcome = await reviews.GenerateSingleCommentaryAsync(u.UserId, tournamentId, handId, u.Language, cancellationToken);
        return outcome.View is { } view
            ? TypedResults.Ok(new HandCommentaryResponse(view.Report.Verdict, view.Report.Analysis, view.Report.Suggestion))
            : CoachingEndpoints.Problem(outcome.Failure!.Value, "COMMENTARY_NOT_AVAILABLE");
    }

    private static TournamentReviewResponse ToResponse(TournamentReview review)
    {
        var t = review.Detail.Tournament;
        var d = review.Debrief?.Report.Debrief;
        return new TournamentReviewResponse(
            t.Id,
            t.Name,
            review.Debrief is not null,
            d?.Headline,
            d?.Story,
            d?.Strengths,
            d?.WorkOn,
            review.Hands.Select(h => new ReviewHandResponse(
                h.HandId, h.Index, h.Level, h.Position, h.HeroCards,
                h.StackInBigBlinds, h.NetBigBlinds, h.IsKeyMoment, h.SawFlop,
                h.Commentary is { } c ? new HandCommentaryResponse(c.Verdict, c.Analysis, c.Suggestion) : null)).ToList(),
            review.Hands.Count(h => h.Commentary is not null),
            review.Hands.Count);
    }
}
