using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Errors;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Api.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Authentication");

        // Browser navigation (not XHR): ends on Google's consent page.
        group.MapGet("/login", SignIn).AllowAnonymous();

        group.MapPost("/logout", SignOutAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

        return endpoints;
    }

    private static Results<ChallengeHttpResult, ProblemHttpResult> SignIn(string? returnUrl, string? language)
    {
        var target = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;

        // Only paths of this site: an absolute URL here would make us an open redirector.
        if (!LocalUrl.IsLocal(target))
        {
            return ApiProblems.Validation("returnUrl", "Must be a local path.");
        }

        var properties = new AuthenticationProperties { RedirectUri = target };
        if (UserLanguages.IsSupported(language))
        {
            properties.Items[AuthenticationSetup.LanguageItem] = language;
        }

        return TypedResults.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
    }

    private static async Task<NoContent> SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }
}

internal static class LocalUrl
{
    /// <summary>Same rule as ASP.NET Core MVC: "/path" is local; "//host", "/\host" and control characters are not.</summary>
    public static bool IsLocal(string url) =>
        url.Length > 0
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(char.IsControl);
}
