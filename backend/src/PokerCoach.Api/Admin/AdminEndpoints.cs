using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Api.Admin;

/// <param name="Plan">Pro or Elite; Pro when omitted.</param>
/// <param name="EndsAt">Null: until revoked.</param>
public sealed record GrantRequest(string? Email, Plan? Plan, string? Reason, DateTimeOffset? EndsAt);

public sealed record GrantResponse(Guid UserId, string DisplayName, string? Email, Plan Plan, string Reason, DateTimeOffset? EndsAt, DateTimeOffset GrantedAt);

/// <param name="StripeUrl">The customer in the Stripe dashboard, where refunds and cancellations are done.</param>
public sealed record SubscriberResponse(
    Guid UserId,
    string DisplayName,
    string? Email,
    SubscriptionStatus? Status,
    Plan? Tier,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    Uri StripeUrl);

public sealed record AiTaskUsageResponse(AiTask Task, int Billed, int Failed, int UnknownCost, int Credits, decimal CostUsd, decimal? CostPerCreditUsd);

public sealed record AiUserUsageResponse(Guid? UserId, string? Email, int Credits, decimal CostUsd);

/// <param name="SpendUsd">Settled calls at their real cost plus the ceilings still reserved (ADR-0017).</param>
public sealed record AiUsageResponse(
    DateTimeOffset MonthStart,
    decimal BudgetUsd,
    decimal SpendUsd,
    decimal OpenReservationsUsd,
    IReadOnlyList<AiTaskUsageResponse> Tasks,
    IReadOnlyList<AiUserUsageResponse> TopUsers);

public sealed record InvitationRequest(string? Email, Plan? Plan, string? Reason, DateTimeOffset? GrantEndsAt);

public sealed record InvitationResponse(
    Guid Id,
    string Email,
    Plan Plan,
    string Reason,
    DateTimeOffset? GrantEndsAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset CreatedAt);

public sealed record AdminOverviewResponse(IReadOnlyList<GrantResponse> Grants, IReadOnlyList<SubscriberResponse> Subscribers, IReadOnlyList<InvitationResponse> Invitations);

/// <summary>
/// Administration (ADR-0015): offered Pro access and the list of subscribers. Every route checks that the
/// signed-in account is an administrator (configured emails); others get 403 ADMIN_REQUIRED.
/// </summary>
public static class AdminEndpoints
{
    private const int TopAiUsers = 10;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin").WithTags("Admin").AddEndpointFilter<AdminOnlyFilter>();
        group.MapGet("", GetAsync);
        group.MapGet("/ai-usage", GetAiUsageAsync);
        group.MapPut("/grants", GrantAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapDelete("/grants/{userId:guid}", RevokeAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPost("/invitations", InviteAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapDelete("/invitations/{id:guid}", DeleteInvitationAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        return endpoints;
    }

    private static async Task<Ok<AdminOverviewResponse>> GetAsync(AdminService admin, InvitationService invitationService, CancellationToken cancellationToken)
    {
        var grants = await admin.ListGrantsAsync(cancellationToken);
        var subscribers = await admin.ListSubscribersAsync(cancellationToken);
        var invitations = await invitationService.ListAsync(cancellationToken);
        return TypedResults.Ok(new AdminOverviewResponse(
            [.. grants.Select(g => new GrantResponse(g.UserId, g.DisplayName, g.Email, g.Plan, g.Reason, g.EndsAt, g.GrantedAt))],
            [.. subscribers.Select(s => new SubscriberResponse(
                s.UserId,
                s.DisplayName,
                s.Email,
                s.Status,
                s.Tier,
                s.Interval,
                s.CurrentPeriodEnd,
                s.CancelAtPeriodEnd,
                new Uri($"https://dashboard.stripe.com/customers/{Uri.EscapeDataString(s.CustomerId)}")))],
            [.. invitations.Select(i => new InvitationResponse(i.Id, i.Email, i.Plan, i.Reason, i.GrantEndsAt, i.ExpiresAt, i.AcceptedAt, i.CreatedAt))]));
    }

    /// <summary>The month's AI spend against the budget, cost per credit by task, the heaviest users.</summary>
    private static async Task<Ok<AiUsageResponse>> GetAiUsageAsync(AiUsageGuard guard, CancellationToken cancellationToken)
    {
        var overview = await guard.MonthOverviewAsync(TopAiUsers, cancellationToken);
        var report = overview.Report;
        return TypedResults.Ok(new AiUsageResponse(
            overview.MonthStart,
            overview.BudgetUsd,
            report.SpendUsd,
            report.OpenReservationsUsd,
            [.. report.Tasks.Select(t => new AiTaskUsageResponse(t.Task, t.Billed, t.Failed, t.UnknownCost, t.Credits, t.CostUsd, t.CostPerCreditUsd))],
            [.. report.TopUsers.Select(u => new AiUserUsageResponse(u.UserId, u.Email, u.Credits, u.CostUsd))]));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> GrantAsync(
        GrantRequest request,
        HttpContext context,
        AdminService admin,
        CancellationToken cancellationToken)
    {
        context.User.TryGetUserId(out var adminId);
        var result = await admin.GrantAsync(adminId, request.Email ?? string.Empty, request.Plan ?? Plan.Pro, request.Reason ?? string.Empty, request.EndsAt, cancellationToken);
        return result switch
        {
            GrantResult.Granted => TypedResults.NoContent(),
            GrantResult.UserNotFound => ApiProblems.WithCode(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "No account uses this email."),
            GrantResult.EndInPast => ApiProblems.Validation("endsAt", "Must be in the future."),
            GrantResult.InvalidPlan => ApiProblems.Validation("plan", "Must be pro or elite."),
            _ => ApiProblems.Validation("reason", "Required."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeAsync(Guid userId, AdminService admin, CancellationToken cancellationToken) =>
        await admin.RevokeAsync(userId, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.WithCode(StatusCodes.Status404NotFound, "GRANT_NOT_FOUND", "This account has no offered access.");

    private static async Task<Results<NoContent, ProblemHttpResult>> InviteAsync(
        InvitationRequest request,
        HttpContext context,
        InvitationService invitationService,
        IOptions<AppOptions> app,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        context.User.TryGetUserId(out var adminId);
        var signUpUrl = new Uri(app.Value.BaseUrl(context, environment), "/sign-in").ToString();
        var result = await invitationService.InviteAsync(
            adminId,
            request.Email ?? string.Empty,
            request.Plan ?? Plan.Pro,
            request.Reason ?? string.Empty,
            request.GrantEndsAt,
            expiresIn: null,
            signUpUrl,
            cancellationToken);
        return result switch
        {
            InvitationResult.Sent => TypedResults.NoContent(),
            InvitationResult.AlreadyRegistered => ApiProblems.WithCode(StatusCodes.Status409Conflict, "ALREADY_REGISTERED", "This email already has an account. Use Grant instead."),
            InvitationResult.InvalidEmail => ApiProblems.Validation("email", "Invalid email address."),
            InvitationResult.InvalidPlan => ApiProblems.Validation("plan", "Must be pro, elite or ultimate."),
            InvitationResult.EndInPast => ApiProblems.Validation("grantEndsAt", "Must be in the future."),
            _ => ApiProblems.Validation("reason", "Required."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteInvitationAsync(Guid id, InvitationService invitationService, CancellationToken cancellationToken) =>
        await invitationService.DeleteAsync(id, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.WithCode(StatusCodes.Status404NotFound, "INVITATION_NOT_FOUND", "This invitation does not exist.");

    /// <summary>Signed in (fallback policy) and listed as administrator.</summary>
    private sealed class AdminOnlyFilter(PlanAccess plans) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            if (!http.User.TryGetUserId(out var userId))
            {
                return TypedResults.Unauthorized();
            }

            return await plans.IsAdminAsync(userId, http.RequestAborted)
                ? await next(context)
                : ApiProblems.WithCode(StatusCodes.Status403Forbidden, "ADMIN_REQUIRED", "Administrators only.");
        }
    }
}
