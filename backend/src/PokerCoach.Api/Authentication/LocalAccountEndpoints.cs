using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Api.Authentication;

/// <summary>Configuration section "App".</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// The web app's public address ("https://poker-coach.example"), used in emailed links. Required outside
    /// Development: building links from the request's Host header would let anyone send real reset links
    /// pointing to their own site.
    /// </summary>
    public Uri? PublicUrl { get; set; }
}

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName, string? Language);

public sealed record SignInRequest(string? Email, string? Password);

public sealed record EmailRequest(string? Email, string? Language);

/// <param name="Password">Confirming requires the password chosen at sign-up (ADR-0013).</param>
public sealed record ConfirmEmailRequest(string? Token, string? Password);

public sealed record ResetPasswordRequest(string? Token, string? Password);

/// <summary>
/// Email and password accounts (ADR-0013). Anonymous, but CSRF-checked (the token is issued to anonymous
/// visitors by GET /api/me) and rate-limited per client address. Requests that could reveal whether an email
/// has an account always answer 202.
/// </summary>
public static class LocalAccountEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapLocalAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/password/login", SignInAsync);
        group.MapPost("/email/confirm", ConfirmEmailAsync);
        group.MapPost("/email/resend", ResendAsync);
        group.MapPost("/password/forgot", ForgotAsync);
        group.MapPost("/password/reset", ResetAsync);
        return endpoints;
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> RegisterAsync(
        RegisterRequest request,
        HttpContext context,
        LocalAccountService accounts,
        IOptions<AppOptions> app,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var result = await accounts.RegisterAsync(
            request.Email ?? string.Empty,
            request.Password ?? string.Empty,
            request.DisplayName,
            Language(request.Language),
            Links(context, app.Value, environment),
            cancellationToken);
        return result switch
        {
            RegistrationResult.InvalidEmail => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "INVALID_EMAIL", "Invalid email address."),
            RegistrationResult.WeakPassword => WeakPassword(),
            _ => TypedResults.Accepted((string?)null),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SignInAsync(
        SignInRequest request,
        HttpContext context,
        LocalAccountService accounts,
        CancellationToken cancellationToken)
    {
        var outcome = await accounts.SignInAsync(request.Email ?? string.Empty, request.Password ?? string.Empty, cancellationToken);
        return await CompleteAsync(context, outcome);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        HttpContext context,
        LocalAccountService accounts,
        CancellationToken cancellationToken)
    {
        var outcome = await accounts.ConfirmEmailAsync(request.Token ?? string.Empty, request.Password ?? string.Empty, cancellationToken);
        return await CompleteAsync(context, outcome);
    }

    private static async Task<Accepted> ResendAsync(
        EmailRequest request,
        HttpContext context,
        LocalAccountService accounts,
        IOptions<AppOptions> app,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        await accounts.ResendConfirmationAsync(request.Email ?? string.Empty, Language(request.Language), Links(context, app.Value, environment), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Accepted> ForgotAsync(
        EmailRequest request,
        HttpContext context,
        LocalAccountService accounts,
        IOptions<AppOptions> app,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        await accounts.ForgotPasswordAsync(request.Email ?? string.Empty, Language(request.Language), Links(context, app.Value, environment), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetAsync(
        ResetPasswordRequest request,
        HttpContext context,
        LocalAccountService accounts,
        CancellationToken cancellationToken)
    {
        var outcome = await accounts.ResetPasswordAsync(request.Token ?? string.Empty, request.Password ?? string.Empty, cancellationToken);
        return await CompleteAsync(context, outcome);
    }

    /// <summary>Signs the user in with the same cookie as Google sign-in, or maps the refusal to a stable code.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> CompleteAsync(HttpContext context, LocalSignInOutcome outcome)
    {
        if (outcome.User is { } user)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(AuthenticationSetup.UserIdClaim, user.Id.ToString())],
                CookieAuthenticationDefaults.AuthenticationScheme));
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return TypedResults.NoContent();
        }

        return outcome.Failure switch
        {
            LocalSignInFailure.EmailNotConfirmed => ApiProblems.WithCode(StatusCodes.Status403Forbidden, "EMAIL_NOT_CONFIRMED", "Confirm your email address first."),
            LocalSignInFailure.Locked => ApiProblems.WithCode(StatusCodes.Status429TooManyRequests, "ACCOUNT_LOCKED", "Too many failed attempts. Try again later."),
            LocalSignInFailure.InvalidToken => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "INVALID_TOKEN", "This link is invalid or has expired."),
            LocalSignInFailure.WeakPassword => WeakPassword(),
            _ => ApiProblems.WithCode(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Wrong email or password."),
        };
    }

    private static ProblemHttpResult WeakPassword() =>
        ApiProblems.WithCode(
            StatusCodes.Status400BadRequest,
            "WEAK_PASSWORD",
            "The password is too weak.",
            new Dictionary<string, object?> { ["minLength"] = LocalAccountService.MinPasswordLength });

    private static string Language(string? language) => UserLanguages.IsSupported(language) ? language! : UserLanguages.Default;

    private static AccountLinks Links(HttpContext context, AppOptions app, IHostEnvironment environment)
    {
        if (app.PublicUrl is { } configured)
        {
            return new AccountLinks(configured.ToString());
        }

        // Development only (enforced at startup): the Angular dev server forwards the browser's Host.
        return environment.IsDevelopment()
            ? new AccountLinks($"{context.Request.Scheme}://{context.Request.Host}")
            : throw new InvalidOperationException("App:PublicUrl is required outside Development.");
    }
}
