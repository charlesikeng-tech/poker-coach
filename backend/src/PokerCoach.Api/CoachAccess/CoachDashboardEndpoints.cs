using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Identity;

namespace PokerCoach.Api.CoachAccess;

public sealed record TrendPointResponse(DateOnly Month, decimal? Vpip, decimal? Pfr, decimal? ThreeBet);

public sealed record StudentSummaryResponse(
    Guid BookmarkId,
    Guid InvitationId,
    string StudentName,
    string Plan,
    int LeakCount,
    int Hands,
    decimal? Vpip,
    decimal? Pfr,
    decimal? ThreeBet,
    string? Notes,
    IReadOnlyList<TrendPointResponse> Trend);

public sealed record AddStudentRequest(Guid Token);

public sealed record UpdateNotesRequest(string? Notes);

public static class CoachDashboardEndpoints
{
    public static IEndpointRouteBuilder MapCoachDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/me/coach-students").WithTags("Coach Dashboard");
        group.MapGet("", ListStudentsAsync);
        group.MapPost("", AddStudentAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPatch("{id:guid}", UpdateNotesAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapDelete("{id:guid}", RemoveStudentAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<StudentSummaryResponse>>, UnauthorizedHttpResult>> ListStudentsAsync(
        HttpContext context,
        CoachDashboardService dashboard,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var students = await dashboard.ListStudentsAsync(userId, cancellationToken);
        return TypedResults.Ok((IReadOnlyList<StudentSummaryResponse>)students.Select(ToResponse).ToList());
    }

    private static async Task<Results<Ok<StudentSummaryResponse>, ProblemHttpResult, UnauthorizedHttpResult>> AddStudentAsync(
        AddStudentRequest request,
        HttpContext context,
        CoachDashboardService dashboard,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        try
        {
            var bookmark = await dashboard.AddStudentAsync(userId, request.Token, cancellationToken);
            var students = await dashboard.ListStudentsAsync(userId, cancellationToken);
            var added = students.FirstOrDefault(s => s.BookmarkId == bookmark.Id);
            if (added is null)
            {
                return ApiProblems.Validation("token", "Student could not be resolved.");
            }

            return TypedResults.Ok(ToResponse(added));
        }
        catch (InvalidOperationException ex)
        {
            return ApiProblems.Validation("token", ex.Message);
        }
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateNotesAsync(
        Guid id,
        UpdateNotesRequest request,
        HttpContext context,
        CoachDashboardService dashboard,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        try
        {
            return await dashboard.UpdateStudentNotesAsync(userId, id, request.Notes, cancellationToken)
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        }
        catch (ArgumentException ex)
        {
            return ApiProblems.Validation("notes", ex.Message);
        }
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> RemoveStudentAsync(
        Guid id,
        HttpContext context,
        CoachDashboardService dashboard,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await dashboard.RemoveStudentAsync(userId, id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static StudentSummaryResponse ToResponse(StudentSummary s) =>
        new(s.BookmarkId, s.InvitationId, s.StudentName, s.Plan, s.LeakCount, s.Hands,
            s.Vpip, s.Pfr, s.ThreeBet, s.Notes,
            s.Trend.Select(t => new TrendPointResponse(t.Month, t.Vpip, t.Pfr, t.ThreeBet)).ToList());
}
