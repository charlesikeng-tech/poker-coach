using Microsoft.AspNetCore.Antiforgery;
using PokerCoach.Api.Errors;

namespace PokerCoach.Api.Authentication;

/// <summary>
/// CSRF protection for cookie-authenticated calls (double-submit): the server issues a readable
/// XSRF-TOKEN cookie, Angular's HttpClient echoes it in the X-XSRF-TOKEN header on unsafe requests, and
/// <see cref="AntiforgeryValidationFilter"/> checks it. SameSite=Lax on the session cookie is the first
/// line of defence; this is the second.
/// </summary>
public static class AntiforgeryTokens
{
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>Read by Angular's built-in XSRF support (default cookie name).</summary>
    public const string RequestTokenCookieName = "XSRF-TOKEN";

    internal static IServiceCollection AddApiAntiforgery(this IServiceCollection services, CookieSecurePolicy securePolicy) =>
        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = "pc.antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securePolicy;
        });

    /// <summary>Tokens are bound to the signed-in user: issue them once the session is known.</summary>
    public static void IssueRequestToken(HttpContext context, IAntiforgery antiforgery)
    {
        // Secure-only cookies outside Development: on plain HTTP (a misrouted request) the antiforgery system
        // throws rather than issue them. Nothing to hand out then; forms will be rejected, safely.
        var options = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AntiforgeryOptions>>().Value;
        if (!context.Request.IsHttps && options.Cookie.SecurePolicy == CookieSecurePolicy.Always)
        {
            return;
        }

        var tokens = antiforgery.GetAndStoreTokens(context);
        if (tokens.RequestToken is null)
        {
            return;
        }

        context.Response.Cookies.Append(RequestTokenCookieName, tokens.RequestToken, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
        });
    }
}

/// <summary>Rejects unsafe requests (POST, PUT, PATCH, DELETE) that do not carry a valid anti-forgery token.</summary>
public sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid anti-forgery token.",
                extensions: new Dictionary<string, object?> { ["code"] = ApiErrorCodes.InvalidAntiforgeryToken });
        }

        return await next(context);
    }
}
