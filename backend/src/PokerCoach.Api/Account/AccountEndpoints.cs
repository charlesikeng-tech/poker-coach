using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Api.Account;

/// <param name="IsAdmin">Shows the administration page; every admin route checks it again.</param>
/// <param name="HasPassword">Whether the account has an email and password credential.</param>
/// <param name="HasGoogle">Whether the account has a Google identity linked.</param>
public sealed record MeResponse(Guid Id, string DisplayName, string? Email, string PreferredLanguage, bool IsAdmin, bool HasPassword, bool HasGoogle);

public sealed record UpdatePreferencesRequest(string? Language);

public sealed record ChangeDisplayNameRequest(string? DisplayName);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <param name="Confirm">Must be exactly "DELETE": a deletion is never one stray request away.</param>
public sealed record DeleteAccountRequest(string? Confirm);

/// <param name="Confirm">Must be exactly "RESET": a reset is never one stray request away.</param>
public sealed record ResetDataRequest(string? Confirm);

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/me").WithTags("Account");

        // Anonymous allowed: it answers 401 itself, after issuing the anti-forgery token the sign-in forms need.
        group.MapGet("/", GetMeAsync).AllowAnonymous();
        group.MapPut("/preferences", UpdatePreferencesAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPut("/display-name", ChangeDisplayNameAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPost("/password", ChangePasswordAsync).AddEndpointFilter<AntiforgeryValidationFilter>().RequireRateLimiting(LocalAccountEndpoints.RateLimitPolicy);
        group.MapGet("/export", ExportAsync);
        group.MapPost("/reset", ResetAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapDelete("/", DeleteAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

        return endpoints;
    }

    /// <summary>The web app calls this first: it tells who is signed in and issues the anti-forgery token.</summary>
    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> GetMeAsync(
        HttpContext context,
        UserProfileService profiles,
        AdminOptions admins,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            // Anonymous visitors get a token too: the sign-in and sign-up forms post with it.
            AntiforgeryTokens.IssueRequestToken(context, antiforgery);
            return TypedResults.Unauthorized();
        }

        var profile = await profiles.GetAsync(userId, cancellationToken);
        if (profile is null)
        {
            // Valid cookie for an account that no longer exists: end the session.
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
            AntiforgeryTokens.IssueRequestToken(context, antiforgery);
            return TypedResults.Unauthorized();
        }

        AntiforgeryTokens.IssueRequestToken(context, antiforgery);
        return TypedResults.Ok(new MeResponse(
            profile.Id, profile.DisplayName, profile.Email, profile.PreferredLanguage,
            admins.IsAdminEmail(profile.Email),
            profile.HasPassword,
            profile.LinkedProviders.Contains(IdentityProviders.Google)));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext context,
        LocalAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await accounts.ChangePasswordAsync(
            userId,
            request.CurrentPassword ?? string.Empty,
            request.NewPassword ?? string.Empty,
            cancellationToken);

        return result switch
        {
            ChangePasswordResult.Success => TypedResults.NoContent(),
            ChangePasswordResult.Locked => ApiProblems.WithCode(StatusCodes.Status429TooManyRequests, "ACCOUNT_LOCKED", "Too many failed attempts. Try again later."),
            ChangePasswordResult.WeakPassword => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "WEAK_PASSWORD", "The new password is too weak.",
                new Dictionary<string, object?> { ["minLength"] = LocalAccountService.MinPasswordLength }),
            ChangePasswordResult.NoPasswordAccount => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "NO_PASSWORD_ACCOUNT", "Use the forgotten password link to add a password to your account."),
            _ => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "INVALID_CREDENTIALS", "Wrong current password."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> ChangeDisplayNameAsync(
        ChangeDisplayNameRequest request,
        HttpContext context,
        UserProfileService profiles,
        CancellationToken cancellationToken)
    {
        var name = request.DisplayName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > User.MaxDisplayNameLength)
        {
            return ApiProblems.Validation("displayName", $"Must be 1 to {User.MaxDisplayNameLength} characters.");
        }

        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!await profiles.ChangeDisplayNameAsync(userId, name, cancellationToken))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.NoContent();
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

    /// <summary>Deletes all poker data but keeps the account, identities and subscription.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> ResetAsync(
        [FromBody] ResetDataRequest request,
        HttpContext context,
        AccountDataService accountData,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (!string.Equals(request.Confirm, "RESET", StringComparison.Ordinal))
        {
            return ApiProblems.Validation("confirm", "Must be \"RESET\".");
        }

        await accountData.ResetAsync(userId, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Deletes the account and everything it owns (GDPR erasure), then ends the session.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        // Explicit: minimal APIs never infer a body on DELETE.
        [FromBody] DeleteAccountRequest request,
        HttpContext context,
        AccountDataService accountData,
        SubscriptionService subscriptions,
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

        // Stop charging first: a paying subscription must never outlive its account (ADR-0014).
        if (!await subscriptions.CloseAsync(userId, cancellationToken))
        {
            return ApiProblems.WithCode(StatusCodes.Status502BadGateway, "BILLING_FAILED", "The subscription could not be canceled. Try again; nothing was deleted.");
        }

        await accountData.DeleteAsync(userId, cancellationToken);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }
}
