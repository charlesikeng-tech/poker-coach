using PokerCoach.Application.Coaching;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Subscriptions;

/// <summary>One offer's limits, for the offer table.</summary>
public sealed record PlanOffer(Plan Plan, int? HistoryDays, int MonthlyCredits);

/// <summary>What the subscription page shows. Credits count this calendar month (UTC).</summary>
/// <param name="Tier">The paid offer subscribed to, when there is a subscription.</param>
public sealed record BillingSummary(
    bool BillingEnabled,
    Plan Plan,
    PlanSource Source,
    DateTimeOffset? GrantEndsAt,
    SubscriptionStatus? Status,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool HasCustomer,
    Plan? Tier,
    int MonthlyCredits,
    int CreditsUsedThisMonth,
    IReadOnlyList<PlanOffer> Offers);

public enum BillingFailure
{
    Unavailable,
    AlreadySubscribed,
    NoCustomer,

    /// <summary>A change of offer needs a live subscription; a new one goes through checkout.</summary>
    NoSubscription,
    ProviderFailed,
    UserNotFound,
}

public sealed record BillingRedirect(Uri? Url, BillingFailure? Failure)
{
    public static BillingRedirect To(Uri url) => new(url, null);

    public static BillingRedirect Fail(BillingFailure failure) => new(null, failure);
}

public enum WebhookOutcome
{
    /// <summary>Bad or stale signature: answer 400, Stripe will not retry a forgery.</summary>
    Rejected,

    /// <summary>Nothing we track (no customer, unknown customer): acknowledge.</summary>
    Ignored,

    Synced,
}

/// <summary>
/// Subscriptions through hosted Stripe pages (ADR-0014). Webhooks never apply their payload: they trigger a
/// re-read of the customer's subscriptions, so duplicates and out-of-order delivery converge on Stripe's state.
/// </summary>
public sealed class SubscriptionService(
    ISubscriptionStore store,
    IBillingGateway gateway,
    PlanAccess plans,
    UserProfileService profiles,
    AiUsageGuard aiUsage,
    TimeProvider time)
{
    /// <summary>A record older than this is re-read from Stripe when shown: repairs a missed webhook.</summary>
    public static readonly TimeSpan MaxSyncAge = TimeSpan.FromDays(1);

    public async Task<BillingSummary> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subscription = gateway.IsConfigured ? await store.FindByUserAsync(userId, cancellationToken) : null;
        if (subscription is not null && time.GetUtcNow() - subscription.SyncedAt > MaxSyncAge)
        {
            await TrySyncAsync(subscription, cancellationToken);
        }

        var used = await aiUsage.CreditsUsedThisMonthAsync(userId, cancellationToken);
        var access = await plans.GetAccessAsync(userId, cancellationToken);
        return new BillingSummary(
            gateway.IsConfigured,
            access.Plan,
            access.Source,
            access.GrantEndsAt,
            subscription?.Status,
            subscription?.Interval,
            subscription?.CurrentPeriodEnd,
            subscription?.CancelAtPeriodEnd ?? false,
            subscription is not null,
            subscription?.IsAlive == true ? subscription.Plan : null,
            plans.Limits.For(access.Plan).MonthlyCredits,
            used,
            [.. Enum.GetValues<Plan>().Select(p => new PlanOffer(p, plans.Limits.For(p).HistoryDays, plans.Limits.For(p).MonthlyCredits))]);
    }

    /// <param name="appUrl">The web app's public address (configuration, never the request's Host).</param>
    /// <param name="plan">Pro or Elite.</param>
    public async Task<BillingRedirect> StartCheckoutAsync(Guid userId, Plan plan, BillingInterval interval, string language, Uri appUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appUrl);
        if (plan == Plan.Starter)
        {
            throw new ArgumentOutOfRangeException(nameof(plan), "Only a paid offer can be bought.");
        }

        if (!gateway.IsConfigured)
        {
            return BillingRedirect.Fail(BillingFailure.Unavailable);
        }

        try
        {
            var subscription = await store.FindByUserAsync(userId, cancellationToken);

            // Our record can lag Stripe (a webhook not received yet, or missed): read Stripe before selling a second
            // subscription. If Stripe cannot be read, no checkout rather than a possible duplicate.
            if (subscription is not null && subscription.IsAlive != true)
            {
                await SyncAsync(subscription, cancellationToken);
            }

            // Changing offer (Pro ↔ Elite) happens on Stripe's confirmation page: no second subscription.
            if (subscription?.IsAlive == true)
            {
                return BillingRedirect.Fail(BillingFailure.AlreadySubscribed);
            }

            subscription ??= await OpenCustomerAsync(userId, cancellationToken);
            if (subscription is null)
            {
                return BillingRedirect.Fail(BillingFailure.UserNotFound);
            }

            return BillingRedirect.To(await gateway.CreateCheckoutAsync(
                new CheckoutRequest(
                    subscription.CustomerId,
                    userId,
                    plan,
                    interval,
                    language,
                    new Uri(appUrl, "subscription?checkout=success"),
                    new Uri(appUrl, "subscription?checkout=cancel")),
                cancellationToken));
        }
        catch (BillingGatewayException)
        {
            // The adapter logged the provider's error.
            return BillingRedirect.Fail(BillingFailure.ProviderFailed);
        }
    }

    public async Task<BillingRedirect> OpenPortalAsync(Guid userId, Uri appUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appUrl);
        if (!gateway.IsConfigured)
        {
            return BillingRedirect.Fail(BillingFailure.Unavailable);
        }

        var subscription = await store.FindByUserAsync(userId, cancellationToken);
        if (subscription is null)
        {
            return BillingRedirect.Fail(BillingFailure.NoCustomer);
        }

        try
        {
            return BillingRedirect.To(await gateway.CreatePortalAsync(subscription.CustomerId, new Uri(appUrl, "subscription"), cancellationToken));
        }
        catch (BillingGatewayException)
        {
            // The adapter logged the provider's error.
            return BillingRedirect.Fail(BillingFailure.ProviderFailed);
        }
    }

    /// <summary>
    /// A subscriber moves to another offer or billing period: straight to Stripe's confirmation of that change
    /// rather than the portal's home page. If Stripe refuses the shortcut, the portal still opens.
    /// </summary>
    public async Task<BillingRedirect> ChangePlanAsync(Guid userId, Plan plan, BillingInterval interval, Uri appUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appUrl);
        if (plan is not (Plan.Pro or Plan.Elite))
        {
            throw new ArgumentOutOfRangeException(nameof(plan), plan, "Only a paid offer can be chosen.");
        }

        if (!gateway.IsConfigured)
        {
            return BillingRedirect.Fail(BillingFailure.Unavailable);
        }

        var subscription = await store.FindByUserAsync(userId, cancellationToken);
        if (subscription is not { IsAlive: true, SubscriptionId: { } subscriptionId })
        {
            return BillingRedirect.Fail(BillingFailure.NoSubscription);
        }

        var returnUrl = new Uri(appUrl, "subscription");
        try
        {
            return BillingRedirect.To(await gateway.CreatePlanChangeAsync(subscription.CustomerId, subscriptionId, plan, interval, returnUrl, cancellationToken));
        }
        catch (BillingGatewayException)
        {
            // The adapter logged why; the portal's own plan switch is the way left.
            return await OpenPortalAsync(userId, appUrl, cancellationToken);
        }
    }

    /// <exception cref="BillingGatewayException">Stripe could not be read: answer 5xx so Stripe retries.</exception>
    public async Task<WebhookOutcome> HandleWebhookAsync(string payload, string? signature, CancellationToken cancellationToken)
    {
        if (!gateway.IsConfigured || gateway.ReadWebhook(payload, signature) is not { } billingEvent)
        {
            return WebhookOutcome.Rejected;
        }

        if (billingEvent.CustomerId is not { } customerId)
        {
            return WebhookOutcome.Ignored;
        }

        var subscription = await store.FindByCustomerAsync(customerId, cancellationToken);
        if (subscription is null)
        {
            // A customer created outside the app (dashboard test) or an account deleted since.
            return WebhookOutcome.Ignored;
        }

        await SyncAsync(subscription, cancellationToken);
        return WebhookOutcome.Synced;
    }

    /// <summary>
    /// Before deleting an account: deletes the Stripe customer, which stops any further charge. False when
    /// Stripe failed: the caller must not delete the account (a paying subscription would be orphaned).
    /// </summary>
    public async Task<bool> CloseAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!gateway.IsConfigured || await store.FindByUserAsync(userId, cancellationToken) is not { } subscription)
        {
            return true;
        }

        try
        {
            await gateway.DeleteCustomerAsync(subscription.CustomerId, cancellationToken);
            return true;
        }
        catch (BillingGatewayException)
        {
            return false;
        }
    }

    private async Task<Subscription?> OpenCustomerAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await profiles.GetAsync(userId, cancellationToken) is not { } profile)
        {
            return null;
        }

        var customerId = await gateway.CreateCustomerAsync(userId, profile.Email, cancellationToken);
        var subscription = Subscription.Open(userId, customerId, time.GetUtcNow());
        if (await store.TryAddAsync(subscription, cancellationToken))
        {
            return subscription;
        }

        // A concurrent checkout opened one first: use it (the customer just created stays unused in Stripe).
        return await store.FindByUserAsync(userId, cancellationToken);
    }

    private async Task SyncAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var states = await gateway.ListSubscriptionsAsync(subscription.CustomerId, cancellationToken);
        subscription.Sync(Subscription.Current(states), time.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task TrySyncAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            await SyncAsync(subscription, cancellationToken);
        }
        catch (BillingGatewayException)
        {
            // Showing the last known state beats failing the page.
        }
    }
}
