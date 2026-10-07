using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PokerCoach.Infrastructure;

namespace PokerCoach.Api.Health;

public static class HealthEndpoints
{
    /// <summary>
    /// <c>/health/live</c>: the process answers (no dependency checked, so a database outage never
    /// triggers a restart loop). <c>/health/ready</c>: dependencies are reachable, traffic can be routed.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .ExcludeFromDescription();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthCheckTags.Ready),
        }).ExcludeFromDescription();

        return endpoints;
    }
}
