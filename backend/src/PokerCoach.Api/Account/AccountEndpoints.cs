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

/// <param name="Confirm">Must be exactly "DELETE": a deletion is never one stray request away.</param>
public sealed record DeleteAccountRequest(string? Confirm);

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/me").WithTags("Account");

        group.MapGet("/", GetMeAsync);
        group.MapPut("/preferences", UpdatePreferencesAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapGet("/export", ExportAsync);
        group.MapDelete("/", DeleteAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

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

    /// <summary>
    /// Everything stored about the player, as a zip (GDPR access and portability). Built in a temporary
    /// file first: zip entries are written synchronously, which Kestrel forbids on the response stream.
    /// </summary>
    private static async Task<Results<FileStreamHttpResult, UnauthorizedHttpResult>> ExportAsync(
        HttpContext context,
        AccountDataService accountData,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var file = new FileStream(
            Path.Combine(Path.GetTempPath(), $"poker-coach-export-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            if (!await accountData.WriteExportAsync(userId, file, cancellationToken))
            {
                await file.DisposeAsync();
                return TypedResults.Unauthorized();
            }

            file.Position = 0;
            // The result disposes the stream once sent, which deletes the temporary file.
            return TypedResults.File(file, "application/zip", $"poker-coach-export-{time.GetUtcNow():yyyy-MM-dd}.zip");
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>Deletes the account and everything it owns (GDPR erasure), then ends the session.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        DeleteAccountRequest request,
        HttpContext context,
        AccountDataService accountData,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!string.Equals(request.Confirm, "DELETE", StringComparison.Ordinal))
        {
            return ApiProblems.Validation("confirm", "Must be \"DELETE\".");
        }

        await accountData.DeleteAsync(userId, cancellationToken);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }
}
