using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Api.Billing;

/// <summary>
/// Features opened by the offers (ADR-0016), checked on the server: the web app's locked states are only a
/// convenience. A refusal is 403 PLAN_REQUIRED with the feature and the lowest offer that opens it.
/// </summary>
public static class PlanGates
{
    /// <summary>The whole endpoint needs the feature.</summary>
    public static TBuilder RequiresFeature<TBuilder>(this TBuilder builder, Feature feature)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!http.User.TryGetUserId(out var userId))
            {
                return TypedResults.Unauthorized();
            }

            var plans = http.RequestServices.GetRequiredService<PlanAccess>();
            return (await plans.GetPlanAsync(userId, http.RequestAborted)).Allows(feature)
                ? await next(context)
                : Required(feature);
        });

    public static ProblemHttpResult Required(Feature feature) =>
        ApiProblems.WithCode(
            StatusCodes.Status403Forbidden,
            "PLAN_REQUIRED",
            "This feature is not in your plan.",
            new Dictionary<string, object?>
            {
                ["feature"] = JsonNaming(feature.ToString()),
                ["plan"] = JsonNaming(PlanFeatures.MinimumPlan(feature).ToString()),
            });

    private static string JsonNaming(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
