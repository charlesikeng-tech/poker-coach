using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Billing;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Leaks;
using PokerCoach.Api.Progress;
using PokerCoach.Api.Statistics;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Progress;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Api.CoachAccess;

/// <param name="StudentName">Display name, so the coach knows whose dashboard they are viewing.</param>
/// <param name="Plan">The student's plan tier.</param>
public sealed record CoachStudentResponse(string StudentName, string Plan);

/// <param name="Label">Human label the student gave this invitation.</param>
/// <param name="ExpiresAt">Null means no expiry.</param>
/// <param name="RevokedAt">Non-null once the student has revoked this invitation.</param>
public sealed record CoachInvitationResponse(Guid Id, string Label, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt);

public sealed record CreateInvitationRequest(string? Label, DateTimeOffset? ExpiresAt);

public static class CoachAccessEndpoints
{
    public static IEndpointRouteBuilder MapCoachAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Student: manage their own invitations.
        var invitations = endpoints.MapGroup("/api/me/coach-invitations").WithTags("Coach Access");
        invitations.MapGet("", ListInvitationsAsync);
        invitations.MapPost("", CreateInvitationAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        invitations.MapDelete("{id:guid}", RevokeInvitationAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

        // Coach: read-only access to the student's data via a token.
        var coach = endpoints.MapGroup("/api/coach/{token:guid}").WithTags("Coach Access");
        coach.MapGet("", GetStudentAsync);
        coach.MapGet("/leaks", GetLeaksAsync);
        coach.MapGet("/statistics", GetStatisticsAsync);
        coach.MapGet("/progress", GetProgressAsync);

        return endpoints;
    }

    // --- Student: manage invitations ---

    private static async Task<Results<Ok<IReadOnlyList<CoachInvitationResponse>>, UnauthorizedHttpResult>> ListInvitationsAsync(
        HttpContext context,
        CoachInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var list = await invitationService.ListAsync(userId, cancellationToken);
        return TypedResults.Ok((IReadOnlyList<CoachInvitationResponse>)list.Select(ToResponse).ToList());
    }

    private static async Task<Results<Ok<CoachInvitationResponse>, ProblemHttpResult, UnauthorizedHttpResult>> CreateInvitationAsync(
        CreateInvitationRequest request,
        HttpContext context,
        CoachInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var label = request.Label?.Trim() ?? string.Empty;
        if (label.Length == 0 || label.Length > CoachInvitation.MaxLabelLength)
        {
            return ApiProblems.Validation("label", $"Must be 1 to {CoachInvitation.MaxLabelLength} characters.");
        }

        var invitation = await invitationService.CreateAsync(userId, label, request.ExpiresAt, cancellationToken);
        return TypedResults.Ok(ToResponse(invitation));
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> RevokeInvitationAsync(
        Guid id,
        HttpContext context,
        CoachInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await invitationService.RevokeAsync(userId, id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    // --- Coach: read student data via token ---

    private static async Task<Results<Ok<CoachStudentResponse>, ForbidHttpResult, UnauthorizedHttpResult>> GetStudentAsync(
        Guid token,
        HttpContext context,
        CoachInvitationService invitationService,
        UserProfileService profiles,
        PlanAccess plans,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out _))
        {
            return TypedResults.Unauthorized();
        }

        var studentId = await invitationService.ResolveStudentIdAsync(token, cancellationToken);
        if (studentId is null)
        {
            return TypedResults.Forbid();
        }

        var profile = await profiles.GetAsync(studentId.Value, cancellationToken);
        if (profile is null)
        {
            return TypedResults.Forbid();
        }

        var plan = await plans.GetPlanAsync(studentId.Value, cancellationToken);
        return TypedResults.Ok(new CoachStudentResponse(profile.DisplayName, plan.ToString()));
    }

    private static async Task<Results<Ok<LeakAnalysisResponse>, ProblemHttpResult, ForbidHttpResult, UnauthorizedHttpResult>> GetLeaksAsync(
        Guid token,
        HttpContext context,
        CoachInvitationService invitationService,
        PlanAccess plans,
        ILeakService leaks,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? format = null,
        string? gameType = null)
    {
        if (!context.User.TryGetUserId(out _))
        {
            return TypedResults.Unauthorized();
        }

        var studentId = await invitationService.ResolveStudentIdAsync(token, cancellationToken);
        if (studentId is null)
        {
            return TypedResults.Forbid();
        }

        if (from is not null && to is not null && from >= to)
        {
            return ApiProblems.Validation("to", "Must be after 'from'.");
        }

        if (!Statistics.TableFormatQuery.TryParse(format, out var tableFormat))
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (!Statistics.GameTypeQuery.TryParse(gameType, out var parsedGameType))
        {
            return ApiProblems.Validation("gameType", "Must be tournament or cashGame.");
        }

        from = await plans.ClampFromAsync(studentId.Value, from, cancellationToken);
        var analysis = await leaks.GetAsync(studentId.Value, tableFormat, from, to, cancellationToken, parsedGameType);

        var advanced = (await plans.GetPlanAsync(studentId.Value, cancellationToken)).Allows(Feature.AdvancedLeaks);
        var shown = analysis.Report.Leaks.Where(l => advanced || !l.Stat.IsPostflop()).ToList();
        return TypedResults.Ok(new LeakAnalysisResponse(
            shown.Select(l => new LeakResponse(
                l.Stat, l.Position, l.Direction, l.Confidence, l.Rate, l.Made, l.Opportunities,
                l.IntervalLow, l.IntervalHigh, new ReferenceRangeResponse(l.Range.Min, l.Range.Max))).ToList(),
            analysis.Report.UnderSampled
                .Where(u => advanced || !u.Stat.IsPostflop())
                .Select(u => new UnderSampledResponse(u.Stat, u.Position, u.Opportunities, u.Needed)).ToList(),
            analysis.Hands,
            analysis.Sample.Tournaments,
            analysis.Sample.CompleteTournaments,
            analysis.PendingHands,
            analysis.ReferenceVersion,
            analysis.Format,
            new FormatCountsResponse(analysis.HandsByFormat.SixMax, analysis.HandsByFormat.FullRing),
            analysis.Report.Leaks.Count - shown.Count));
    }

    private static async Task<Results<Ok<StatisticsResponse>, ProblemHttpResult, ForbidHttpResult, UnauthorizedHttpResult>> GetStatisticsAsync(
        Guid token,
        HttpContext context,
        CoachInvitationService invitationService,
        PlanAccess plans,
        StatisticsService statistics,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? format = null,
        string? gameType = null)
    {
        if (!context.User.TryGetUserId(out _))
        {
            return TypedResults.Unauthorized();
        }

        var studentId = await invitationService.ResolveStudentIdAsync(token, cancellationToken);
        if (studentId is null)
        {
            return TypedResults.Forbid();
        }

        if (!Statistics.TableFormatQuery.TryParse(format, out var tableFormat))
        {
            return ApiProblems.Validation("format", "Must be sixMax or fullRing.");
        }

        if (!Statistics.GameTypeQuery.TryParse(gameType, out var parsedGameType))
        {
            return ApiProblems.Validation("gameType", "Must be tournament or cashGame.");
        }

        from = await plans.ClampFromAsync(studentId.Value, from, cancellationToken);
        return TypedResults.Ok(StatisticsEndpoints.ToResponse(
            await statistics.GetAsync(studentId.Value, new Application.Statistics.StatisticsFilter(from, to, null, null, false, tableFormat, null, parsedGameType, null), cancellationToken)));
    }

    private static async Task<Results<Ok<ProgressResponse>, ProblemHttpResult, ForbidHttpResult, UnauthorizedHttpResult>> GetProgressAsync(
        Guid token,
        HttpContext context,
        CoachInvitationService invitationService,
        ProgressService progress,
        CancellationToken cancellationToken,
        string? gameType = null,
        string? subFormat = null)
    {
        if (!context.User.TryGetUserId(out _))
        {
            return TypedResults.Unauthorized();
        }

        var studentId = await invitationService.ResolveStudentIdAsync(token, cancellationToken);
        if (studentId is null)
        {
            return TypedResults.Forbid();
        }

        if (!Statistics.GameTypeQuery.TryParse(gameType, out var parsedGameType))
        {
            return ApiProblems.Validation("gameType", "Must be tournament or cashGame.");
        }

        if (!Statistics.TournamentSubFormatQuery.TryParse(subFormat, out var parsedSubFormat))
        {
            return ApiProblems.Validation("subFormat", "Must be mtt, rebuyMtt or sng.");
        }

        return TypedResults.Ok(ProgressEndpoints.ToResponse(
            await progress.GetAsync(studentId.Value, parsedGameType ?? GameType.Tournament, parsedSubFormat, cancellationToken)));
    }

    private static CoachInvitationResponse ToResponse(Domain.Identity.CoachInvitation i) =>
        new(i.Id, i.Label, i.CreatedAt, i.ExpiresAt, i.RevokedAt);
}
