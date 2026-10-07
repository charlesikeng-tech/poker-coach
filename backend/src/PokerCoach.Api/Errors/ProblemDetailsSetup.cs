using System.Diagnostics;

namespace PokerCoach.Api.Errors;

public static class ProblemDetailsSetup
{
    /// <summary>
    /// Every error leaves the API as RFC 9457 problem details carrying a stable <c>code</c> and a
    /// <c>traceId</c> the user can quote to support. Exception messages and stack traces are never
    /// included outside Development (where the developer exception page takes over).
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;

            // Endpoints that know a more precise code set it themselves; only fill the gap.
            problem.Extensions.TryAdd("code", ApiErrorCodes.FromStatusCode(status));
            problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        });
}
