using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Api.Hosting;

/// <summary>
/// Production hosting (ADR-0011): one container serves the API and the compiled web app from the same
/// origin, so the session cookie, anti-forgery and CSP stay simple (no CORS, no third-party cookie).
/// </summary>
public static partial class WebHosting
{
    /// <summary>
    /// Scripts only from our origin. Styles allow inline: Angular injects component styles at runtime, and a
    /// per-request nonce would mean rendering index.html on the server. The Google sign-in is a top-level
    /// navigation, not a form or a frame, so it needs no exception.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
        + "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; "
        + "frame-ancestors 'none'";

    private const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>Headers every response carries, API included.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            return next(context);
        });

    /// <summary>
    /// Serves the compiled web app from wwwroot when present (the container image; local development uses
    /// the Angular dev server instead). Hashed bundles are cached for a year, everything else revalidated.
    /// </summary>
    public static bool UseWebApp(this WebApplication app)
    {
        if (!Directory.Exists(app.Environment.WebRootPath))
        {
            return false;
        }

        app.UseStaticFiles(StaticFiles());
        return true;
    }

    /// <summary>
    /// Client-side routes (/tournaments/…, /sign-in) answer index.html. API, health and file-like paths are
    /// excluded: an unknown API route stays a 404, a missing bundle is not answered with HTML.
    /// </summary>
    public static void MapWebAppFallback(this WebApplication app)
    {
        // The root separately: an empty catch-all value never satisfies the regex constraint.
        foreach (var pattern in (string[])["/", "{*path:nonfile:regex(^(?!api(/|$)|health(/|$)|signin-google$).*$)}"])
        {
            app.MapFallbackToFile(pattern, "index.html", StaticFiles())
                .AllowAnonymous()
                .ExcludeFromDescription();
        }
    }

    /// <summary>
    /// <c>migrate</c> mode: applies pending EF Core migrations and exits. Run by the release pipeline before
    /// the new version takes traffic, never at application startup (two instances would race, and a
    /// failed migration would crash-loop the app). EF Core takes a database lock while migrating.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        app.Logger.LogInformation("Applying {Count} pending migrations: {Migrations}", pending.Count, pending);
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("Database schema up to date.");
    }

    private static StaticFileOptions StaticFiles() => new()
    {
        OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl =
                HashedBundle().IsMatch(context.File.Name) ? Immutable : "no-cache",
    };

    /// <summary>Angular's output hashing: main-ABCD1234.js, styles-ABCD1234.css, media/font-ABCD1234.woff2.</summary>
    [GeneratedRegex(@"-[A-Z0-9]{8}\.[a-z0-9]+$")]
    private static partial Regex HashedBundle();
}
