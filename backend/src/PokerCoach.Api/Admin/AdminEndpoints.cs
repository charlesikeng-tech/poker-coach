using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
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

public sealed record AdminOverviewResponse(IReadOnlyList<GrantResponse> Grants, IReadOnlyList<SubscriberResponse> Subscribers);

/// <summary>
/// Administration (ADR-0015): offered Pro access and the list of subscribers. Every route checks that the
/// signed-in account is an administrator (configured emails); others get 403 ADMIN_REQUIRED.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin").WithTags("Admin").AddEndpointFilter<AdminOnlyFilter>();
        group.MapGet("", GetAsync);
        group.MapPut("/grants", GrantAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapDelete("/grants/{userId:guid}", RevokeAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        return endpoints;
    }

    private static async Task<Ok<AdminOverviewResponse>> GetAsync(AdminService admin, CancellationToken cancellationToken)
    {
        var grants = await admin.ListGrantsAsync(cancellationToken);
        var subscribers = await admin.ListSubscribersAsync(cancellationToken);
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
                new Uri($"https://dashboard.stripe.com/customers/{Uri.EscapeDataString(s.CustomerId)}")))]));
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
