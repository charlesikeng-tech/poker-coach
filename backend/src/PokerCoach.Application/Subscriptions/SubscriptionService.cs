using PokerCoach.Application.Coaching;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Subscriptions;

/// <summary>What the subscription page shows. <c>Used</c> counts this calendar month (UTC).</summary>
public sealed record BillingSummary(
    bool BillingEnabled,
    Plan Plan,
    SubscriptionStatus? Status,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool HasCustomer,
    int FreeHistoryDays,
    int FreeMonthlyExplanations,
    int ExplanationsUsedThisMonth);

public enum BillingFailure
{
    Unavailable,
    AlreadySubscribed,
    NoCustomer,
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
    ICoachingStore coachingLedger,
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

        var now = time.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var used = await coachingLedger.CountCallsSinceAsync(userId, LeakCoachService.Purpose, monthStart, cancellationToken);
        return new BillingSummary(
            gateway.IsConfigured,
            await plans.GetPlanAsync(userId, cancellationToken),
            subscription?.Status,
            subscription?.Interval,
            subscription?.CurrentPeriodEnd,
            subscription?.CancelAtPeriodEnd ?? false,
            subscription is not null,
            plans.FreeLimits.HistoryDays,
            plans.FreeLimits.MonthlyExplanations,
            used);
    }

    /// <param name="appUrl">The web app's public address (configuration, never the request's Host).</param>
    public async Task<BillingRedirect> StartCheckoutAsync(Guid userId, BillingInterval interval, string language, Uri appUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appUrl);
        if (!gateway.IsConfigured)
        {
            return BillingRedirect.Fail(BillingFailure.Unavailable);
        }

        try
        {
            var subscription = await store.FindByUserAsync(userId, cancellationToken);
            if (subscription?.Plan == Plan.Pro)
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
