using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Api.Account;

public sealed record MeResponse(Guid Id, string DisplayName, string? Email, string PreferredLanguage);

public sealed record UpdatePreferencesRequest(string? Language);

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/me").WithTags("Account");

        group.MapGet("/", GetMeAsync);
        group.MapPut("/preferences", UpdatePreferencesAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

        return endpoints;
    }

    /// <summary>The web app calls this first: it tells who is signed in and issues the anti-forgery token.</summary>
    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> GetMeAsync(
        HttpContext context,
        UserProfileService profiles,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var profile = await profiles.GetAsync(userId, cancellationToken);
        if (profile is null)
        {
            // Valid cookie for an account that no longer exists: end the session.
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return TypedResults.Unauthorized();
        }

        AntiforgeryTokens.IssueRequestToken(context, antiforgery);
        return TypedResults.Ok(new MeResponse(profile.Id, profile.DisplayName, profile.Email, profile.PreferredLanguage));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> UpdatePreferencesAsync(
        UpdatePreferencesRequest request,
        HttpContext context,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        if (!UserLanguages.IsSupported(request.Language))
        {
            return ApiProblems.Validation("language", $"Must be one of: {string.Join(", ", UserLanguages.All)}.");
        }

        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!await profiles.ChangePreferredLanguageAsync(userId, request.Language!, cancellationToken))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.NoContent();
    }
}
