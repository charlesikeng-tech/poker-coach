using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Api.Authentication;

/// <summary>
/// Backend-for-frontend authentication (ADR-0004): the server runs the Google OpenID Connect flow and
/// keeps the session in an HttpOnly cookie. The browser never holds a token.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>Claim holding our internal user id: the only claim kept in the session cookie.</summary>
    public const string UserIdClaim = "pc_uid";

    /// <summary>Rotated on every sign-in. Mismatch → session rejected (account sharing / stolen cookie mitigation).</summary>
    public const string SessionSerialClaim = "pc_ser";

    /// <summary>Language the visitor was using before sign-in, carried through the Google round trip.</summary>
    internal const string LanguageItem = "pc_language";

    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var googleSection = configuration.GetSection(GoogleAuthenticationOptions.SectionName);
        services.AddOptions<GoogleAuthenticationOptions>()
            .Bind(googleSection)
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ClientId) && !string.IsNullOrWhiteSpace(o.ClientSecret),
                "Missing Google OAuth client. Set Authentication:Google:ClientId and Authentication:Google:ClientSecret "
                + "with `dotnet user-secrets` (or the Authentication__Google__* environment variables).")
            .ValidateOnStart();

        // HTTPS is mandatory outside Development; local development runs on http://localhost.
        var securePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "pc.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = securePolicy;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;

                // An API answers 401/403; the web app decides where to send the user.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = ValidateSessionSerialAsync;
            })
            .AddGoogle(options =>
            {
                var google = googleSection.Get<GoogleAuthenticationOptions>() ?? new GoogleAuthenticationOptions();
                options.ClientId = google.ClientId;
                options.ClientSecret = google.ClientSecret;
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.UsePkce = true;
                options.SaveTokens = false;

                // Google may return an address it has not verified (an account created with another
                // provider's email). Only a verified one may identify, link or administer an account.
                options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "email_verified");

                // Google answers with a top-level GET redirect: Lax is enough and works on plain-http localhost.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = securePolicy;

                options.Events.OnTicketReceived = OnGoogleTicketReceivedAsync;
                options.Events.OnRemoteFailure = context =>
                {
                    // Cancelled consent or provider error: back to the sign-in page, without details.
                    context.Response.Redirect("/sign-in?error=google");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        // Secure by default: every endpoint requires a signed-in user unless it opts out explicitly.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddApiAntiforgery(securePolicy);
        return services;
    }

    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirst(UserIdClaim)?.Value, out userId);

    // Checked at most once every 30 minutes per session; more frequent for a cookie stolen mid-day, less
    // friction for a normal user browsing without gaps.
    private static readonly TimeSpan SerialValidationInterval = TimeSpan.FromMinutes(30);
    private const string SerialValidatedAtProperty = "pc_svt";

    private const string EmailVerifiedClaim = "email_verified";

    private static async Task ValidateSessionSerialAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal is null || !principal.TryGetUserId(out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var serialClaim = principal.FindFirst(SessionSerialClaim)?.Value;
        if (serialClaim is null || !Guid.TryParse(serialClaim, out var cookieSerial))
        {
            // Cookie predates this feature: force re-login once to pick up the serial.
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var time = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
        var now = time.GetUtcNow();

        if (context.Properties.Items.TryGetValue(SerialValidatedAtProperty, out var lastStr)
            && DateTimeOffset.TryParse(lastStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
            && now - last < SerialValidationInterval)
        {
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<IUserAccountStore>();
        var user = await store.FindByIdAsync(userId, context.HttpContext.RequestAborted);

        if (user is null || user.SessionSerial != cookieSerial)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        context.Properties.Items[SerialValidatedAtProperty] = now.ToString("O");
        context.ShouldRenew = true;
    }

    private static async Task OnGoogleTicketReceivedAsync(TicketReceivedContext context)
    {
        var google = context.Principal;
        var subject = google?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (google is null || string.IsNullOrEmpty(subject))
        {
            context.Fail("Google did not return a subject identifier.");
            return;
        }

        string? language = null;
        context.Properties?.Items.TryGetValue(LanguageItem, out language);

        var emailVerified = string.Equals(google.FindFirst(EmailVerifiedClaim)?.Value, "true", StringComparison.OrdinalIgnoreCase);
        var signIn = new ExternalSignIn(
            IdentityProviders.Google,
            subject,
            emailVerified ? google.FindFirst(ClaimTypes.Email)?.Value : null,
            google.FindFirst(ClaimTypes.Name)?.Value,
            language);

        // Event callbacks have no constructor injection: resolving from the request scope is the supported way.
        var signInService = context.HttpContext.RequestServices.GetRequiredService<ExternalSignInService>();
        var user = await signInService.SignInAsync(signIn, context.HttpContext.RequestAborted);

        // Keep only our own id and session serial in the cookie: no Google claims, no email.
        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(UserIdClaim, user.Id.ToString()), new Claim(SessionSerialClaim, user.SessionSerial.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
