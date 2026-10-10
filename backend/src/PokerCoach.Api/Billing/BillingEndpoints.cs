using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Api.Billing;

/// <param name="Plan">What the account has now: drives limits and upgrade prompts.</param>
/// <param name="BillingEnabled">False: billing is off on this server and every account has Pro.</param>
/// <param name="CurrentPeriodEnd">Renewal date, or end date when <paramref name="CancelAtPeriodEnd"/>.</param>
/// <param name="ExplanationsUsedThisMonth">New leak explanations generated this calendar month (UTC).</param>
public sealed record BillingResponse(
    bool BillingEnabled,
    Plan Plan,
    SubscriptionStatus? Status,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool CanManage,
    FreePlanResponse Free,
    int ExplanationsUsedThisMonth);

public sealed record FreePlanResponse(int HistoryDays, int MonthlyExplanations);

public sealed record CheckoutRequestBody(BillingInterval? Interval);

/// <summary>A Stripe-hosted page to send the browser to.</summary>
public sealed record RedirectResponse(Uri Url);

/// <summary>Subscriptions (ADR-0014): plan summary, Stripe Checkout and portal, Stripe webhooks.</summary>
public static partial class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/billing").WithTags("Billing");
        group.MapGet("", GetAsync);
        group.MapPost("/checkout", CheckoutAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPost("/portal", PortalAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

        // Called by Stripe: no session, no CSRF token; the signature is the authentication.
        group.MapPost("/webhook", WebhookAsync).AllowAnonymous().ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<Results<Ok<BillingResponse>, UnauthorizedHttpResult>> GetAsync(
        HttpContext context,
        SubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var s = await subscriptions.GetAsync(userId, cancellationToken);
        return TypedResults.Ok(new BillingResponse(
            s.BillingEnabled,
            s.Plan,
            s.Status,
            s.Interval,
            s.CurrentPeriodEnd,
            s.CancelAtPeriodEnd,
            s.BillingEnabled && s.HasCustomer,
            new FreePlanResponse(s.FreeHistoryDays, s.FreeMonthlyExplanations),
            s.ExplanationsUsedThisMonth));
    }

    private static async Task<Results<Ok<RedirectResponse>, ProblemHttpResult, UnauthorizedHttpResult>> CheckoutAsync(
        CheckoutRequestBody request,
        HttpContext context,
        SubscriptionService subscriptions,
        UserProfileService profiles,
        IOptions<AppOptions> app,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (request.Interval is not { } interval || !Enum.IsDefined(interval))
        {
            return ApiProblems.Validation("interval", "Must be month or year.");
        }

        var language = (await profiles.GetAsync(userId, cancellationToken))?.PreferredLanguage ?? UserLanguages.Default;
        var result = await subscriptions.StartCheckoutAsync(userId, interval, language, app.Value.BaseUrl(context, environment), cancellationToken);
        return Respond(result);
    }

    private static async Task<Results<Ok<RedirectResponse>, ProblemHttpResult, UnauthorizedHttpResult>> PortalAsync(
        HttpContext context,
        SubscriptionService subscriptions,
        IOptions<AppOptions> app,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return Respond(await subscriptions.OpenPortalAsync(userId, app.Value.BaseUrl(context, environment), cancellationToken));
    }

    private static async Task<IResult> WebhookAsync(
        HttpContext context,
        SubscriptionService subscriptions,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        // Stripe events are a few KB; refuse anything absurd before reading it.
        if (context.Request.ContentLength is > 512 * 1024)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        using var reader = new StreamReader(context.Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var logger = loggers.CreateLogger(typeof(BillingEndpoints));
        try
        {
            var outcome = await subscriptions.HandleWebhookAsync(payload, context.Request.Headers["Stripe-Signature"], cancellationToken);
            if (outcome == WebhookOutcome.Rejected)
            {
                LogRejected(logger);
                return TypedResults.BadRequest();
            }

            return TypedResults.Ok();
        }
        catch (BillingGatewayException)
        {
            // Stripe could not be read back: 503 makes Stripe retry the event later.
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static Results<Ok<RedirectResponse>, ProblemHttpResult, UnauthorizedHttpResult> Respond(BillingRedirect result) =>
        result switch
        {
            { Url: { } url } => TypedResults.Ok(new RedirectResponse(url)),
            { Failure: BillingFailure.AlreadySubscribed } => ApiProblems.WithCode(StatusCodes.Status409Conflict, "ALREADY_SUBSCRIBED", "This account already has Pro: manage it from the portal."),
            { Failure: BillingFailure.NoCustomer } => ApiProblems.WithCode(StatusCodes.Status404NotFound, "NO_SUBSCRIPTION", "This account has never subscribed."),
            { Failure: BillingFailure.UserNotFound } => TypedResults.Unauthorized(),
            { Failure: BillingFailure.Unavailable } => ApiProblems.WithCode(StatusCodes.Status503ServiceUnavailable, "BILLING_UNAVAILABLE", "Billing is not enabled on this server."),
            _ => ApiProblems.WithCode(StatusCodes.Status502BadGateway, "BILLING_FAILED", "The payment provider could not be reached. Try again."),
        };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Billing webhook refused: missing, invalid or expired signature.")]
    private static partial void LogRejected(ILogger logger);
}
