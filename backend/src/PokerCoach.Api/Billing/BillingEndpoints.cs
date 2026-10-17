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
/// <param name="Source">Why: a subscription, an offered access (with <paramref name="GrantEndsAt"/>), administrator, billing off.</param>
/// <param name="BillingEnabled">False: billing is off on this server and every account has Pro.</param>
/// <param name="CurrentPeriodEnd">Renewal date, or end date when <paramref name="CancelAtPeriodEnd"/>.</param>
/// <param name="Tier">The paid offer subscribed to, when a subscription is alive.</param>
/// <param name="MonthlyCredits">AI credits of the current plan per calendar month (UTC).</param>
/// <param name="PurchasedCreditsBalance">Remaining credits from one-time top-up packs.</param>
/// <param name="Offers">Every offer with its limits, for the offer table.</param>
public sealed record BillingResponse(
    bool BillingEnabled,
    Plan Plan,
    PlanSource Source,
    DateTimeOffset? GrantEndsAt,
    SubscriptionStatus? Status,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool CanManage,
    Plan? Tier,
    int MonthlyCredits,
    int CreditsUsedThisMonth,
    int PurchasedCreditsBalance,
    IReadOnlyList<OfferResponse> Offers);

/// <param name="HistoryDays">Null: the whole history.</param>
public sealed record OfferResponse(Plan Plan, int? HistoryDays, int MonthlyCredits);

/// <param name="Plan">Pro or Elite.</param>
public sealed record CheckoutRequestBody(Plan? Plan, BillingInterval? Interval);

/// <param name="Credits">50 (Recharge) or 120 (Session).</param>
public sealed record TopUpRequestBody(int Credits);

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
        group.MapPost("/topup", TopUpAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPost("/portal", PortalAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        group.MapPost("/change", ChangeAsync).AddEndpointFilter<AntiforgeryValidationFilter>();

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
            s.Source,
            s.GrantEndsAt,
            s.Status,
            s.Interval,
            s.CurrentPeriodEnd,
            s.CancelAtPeriodEnd,
            s.BillingEnabled && s.HasCustomer,
            s.Tier,
            s.MonthlyCredits,
            s.CreditsUsedThisMonth,
            s.PurchasedCreditsBalance,
            [.. s.Offers.Select(o => new OfferResponse(o.Plan, o.HistoryDays, o.MonthlyCredits))]));
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

        // Pro when omitted: clients written before ADR-0016 only offered Pro.
        var plan = request.Plan ?? Plan.Pro;
        if (plan is not (Plan.Pro or Plan.Elite))
        {
            return ApiProblems.Validation("plan", "Must be pro or elite.");
        }

        var language = (await profiles.GetAsync(userId, cancellationToken))?.PreferredLanguage ?? UserLanguages.Default;
        var result = await subscriptions.StartCheckoutAsync(userId, plan, interval, language, app.Value.BaseUrl(context, environment), cancellationToken);
        return Respond(result);
    }

    private static async Task<Results<Ok<RedirectResponse>, ProblemHttpResult, UnauthorizedHttpResult>> TopUpAsync(
        TopUpRequestBody request,
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

        if (request.Credits is not (50 or 120))
        {
            return ApiProblems.Validation("credits", "Must be 50 or 120.");
        }

        var language = (await profiles.GetAsync(userId, cancellationToken))?.PreferredLanguage ?? UserLanguages.Default;
        var result = await subscriptions.StartTopUpAsync(userId, request.Credits, language, app.Value.BaseUrl(context, environment), cancellationToken);
        return Respond(result);
    }

    /// <summary>
    /// A subscriber switches offer or billing period: the URL of Stripe's confirmation page for that change
    /// (prorated amount shown there), or of the portal if Stripe refuses the shortcut. 404 NO_SUBSCRIPTION without
    /// a live subscription: a first purchase goes through checkout.
    /// </summary>
    private static async Task<Results<Ok<RedirectResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ChangeAsync(
        CheckoutRequestBody request,
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

        if (request.Interval is not { } interval || !Enum.IsDefined(interval))
        {
            return ApiProblems.Validation("interval", "Must be month or year.");
        }

        if (request.Plan is not { } plan || plan is not (Plan.Pro or Plan.Elite))
        {
            return ApiProblems.Validation("plan", "Must be pro or elite.");
        }

        return Respond(await subscriptions.ChangePlanAsync(userId, plan, interval, app.Value.BaseUrl(context, environment), cancellationToken));
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
            { Failure: BillingFailure.NoSubscription } => ApiProblems.WithCode(StatusCodes.Status404NotFound, "NO_SUBSCRIPTION", "This account has no live subscription to change."),
            { Failure: BillingFailure.UserNotFound } => TypedResults.Unauthorized(),
            { Failure: BillingFailure.Unavailable } => ApiProblems.WithCode(StatusCodes.Status503ServiceUnavailable, "BILLING_UNAVAILABLE", "Billing is not enabled on this server."),
            _ => ApiProblems.WithCode(StatusCodes.Status502BadGateway, "BILLING_FAILED", "The payment provider could not be reached. Try again."),
        };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Billing webhook refused: missing, invalid or expired signature.")]
    private static partial void LogRejected(ILogger logger);
}
