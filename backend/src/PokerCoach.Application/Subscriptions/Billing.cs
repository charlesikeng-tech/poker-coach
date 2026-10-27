using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Subscriptions;

/// <summary>History and AI credits of one offer.</summary>
public sealed class PlanLimits
{
    /// <summary>Days of history shown in statistics, leaks, ranges and tournament lists; null: all of it.</summary>
    public int? HistoryDays { get; set; }

    /// <summary>AI credits per calendar month (UTC): an explanation costs 1, a review 2, a debrief 5.</summary>
    public int MonthlyCredits { get; set; }
}

/// <summary>
/// Limits per offer (ADR-0016). Configuration section "Billing:Plans": a pricing test needs no release. What each
/// offer opens besides these limits is the product matrix, <see cref="PlanFeatures"/>.
/// </summary>
public sealed class PlanOptions
{
    public const string SectionName = "Billing:Plans";

    public PlanLimits Starter { get; set; } = new() { HistoryDays = 30, MonthlyCredits = 3 };

    public PlanLimits Pro { get; set; } = new() { MonthlyCredits = 100 };

    public PlanLimits Elite { get; set; } = new() { MonthlyCredits = 400 };

    public PlanLimits Ultimate { get; set; } = new() { MonthlyCredits = 1000 };

    public PlanLimits For(Plan plan) => plan switch
    {
        Plan.Ultimate => Ultimate,
        Plan.Elite => Elite,
        Plan.Pro => Pro,
        _ => Starter,
    };
}

/// <summary>Configuration section "Admin" (ADR-0015).</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Accounts whose email is listed administer the product and always have Elite. Emails reach an account only
    /// once proved (confirmed link, or verified by Google), so listing an address cannot be hijacked by
    /// someone signing up with it.
    /// </summary>
    public IList<string> Emails { get; } = [];

    public bool IsAdminEmail(string? email) =>
        email is not null && Emails.Any(e => string.Equals(EmailAddresses.Normalize(e), EmailAddresses.Normalize(email), StringComparison.Ordinal));
}

/// <summary>A grant as administrators see it.</summary>
public sealed record GrantView(Guid UserId, string DisplayName, string? Email, Plan Plan, string Reason, DateTimeOffset? EndsAt, DateTimeOffset GrantedAt);

/// <summary>A paying (or formerly paying) customer as administrators see it.</summary>
public sealed record SubscriberView(
    Guid UserId,
    string DisplayName,
    string? Email,
    string CustomerId,
    SubscriptionStatus? Status,
    Plan? Tier,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

public interface ISubscriptionStore
{
    Task<Subscription?> FindByUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<Subscription?> FindByCustomerAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>False when the user already has one (a concurrent checkout won): read it again.</summary>
    Task<bool> TryAddAsync(Subscription subscription, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<ProGrant?> FindGrantAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Adds the grant, or replaces the user's current one.</summary>
    Task SaveGrantAsync(ProGrant grant, CancellationToken cancellationToken);

    Task<bool> DeleteGrantAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every grant, ending soonest first, open-ended last.</summary>
    Task<IReadOnlyList<GrantView>> ListGrantsAsync(CancellationToken cancellationToken);

    /// <summary>Accounts with a subscription status (customers who never subscribed are left out), most recent first.</summary>
    Task<IReadOnlyList<SubscriberView>> ListSubscribersAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>A webhook whose signature checked out, reduced to what we act on.</summary>
/// <param name="CustomerId">The customer the event is about; null for events about nothing we track.</param>
/// <param name="CreditUserId">Set for checkout.session.completed with mode=payment: the user to credit.</param>
/// <param name="Credits">Credits purchased in a one-time checkout.</param>
public sealed record BillingEvent(string Id, string Type, string? CustomerId, Guid? CreditUserId = null, int? Credits = null);

/// <summary>The payment provider failed or refused; the message names the provider's error, never card data.</summary>
public sealed class BillingGatewayException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Port to the payment provider (Stripe adapter in Infrastructure). Hosted pages only: no card data here.</summary>
public interface IBillingGateway
{
    /// <summary>False without a secret key: billing is off and every account has Pro (ADR-0014).</summary>
    bool IsConfigured { get; }

    /// <exception cref="BillingGatewayException"/>
    Task<string> CreateCustomerAsync(Guid userId, string? email, CancellationToken cancellationToken);

    /// <summary>A hosted checkout page for a paid offer at that interval.</summary>
    /// <exception cref="BillingGatewayException"/>
    Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>A one-time hosted checkout page for a credit top-up pack.</summary>
    /// <exception cref="BillingGatewayException"/>
    Task<Uri> CreateTopUpCheckoutAsync(TopUpRequest request, CancellationToken cancellationToken);

    /// <exception cref="BillingGatewayException"/>
    Task<Uri> CreatePortalAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken);

    /// <summary>
    /// The portal opened on the confirmation of a change to another offer or billing period: Stripe shows the
    /// prorated amount and applies the change once confirmed (the webhook then re-reads the subscription).
    /// </summary>
    /// <exception cref="BillingGatewayException">Includes a price not configured for that offer and period.</exception>
    Task<Uri> CreatePlanChangeAsync(string customerId, string subscriptionId, Plan plan, BillingInterval interval, Uri returnUrl, CancellationToken cancellationToken);

    /// <summary>Every subscription of the customer, whatever its status.</summary>
    /// <exception cref="BillingGatewayException"/>
    Task<IReadOnlyList<SubscriptionState>> ListSubscriptionsAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>Deletes the customer, which cancels their subscriptions at once. A customer already gone is not an error.</summary>
    /// <exception cref="BillingGatewayException"/>
    Task DeleteCustomerAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>The event if the signature is valid and recent; null otherwise.</summary>
    BillingEvent? ReadWebhook(string payload, string? signatureHeader);
}

public sealed record CheckoutRequest(
    string CustomerId,
    Guid UserId,
    Plan Plan,
    BillingInterval Interval,
    string Language,
    Uri SuccessUrl,
    Uri CancelUrl);

public sealed record TopUpRequest(Guid UserId, int Credits, string Language, Uri SuccessUrl, Uri CancelUrl);

/// <summary>What an account has, and why.</summary>
public sealed record PlanGrant(Plan Plan, PlanSource Source, DateTimeOffset? GrantEndsAt = null);

/// <summary>
/// Which plan a user has and what it opens (ADR-0014 to 0016). Administrators and billing off (development,
/// tests, self-hosting) have Ultimate; otherwise the higher of a live Stripe subscription and an active grant.
/// </summary>
public sealed class PlanAccess(
    ISubscriptionStore store,
    IBillingGateway gateway,
    IUserAccountStore users,
    AdminOptions admins,
    PlanOptions limits,
    TimeProvider time)
{
    public PlanOptions Limits => limits;

    public bool BillingEnabled => gateway.IsConfigured;

    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        admins.Emails.Count > 0 && admins.IsAdminEmail((await users.FindByIdAsync(userId, cancellationToken))?.Email);

    public async Task<PlanGrant> GetAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await IsAdminAsync(userId, cancellationToken))
        {
            return new PlanGrant(Plan.Ultimate, PlanSource.Admin);
        }

        if (!gateway.IsConfigured)
        {
            return new PlanGrant(Plan.Ultimate, PlanSource.BillingOff);
        }

        // The higher of what is paid and what is given (a Pro subscriber given Elite has Elite).
        var paid = (await store.FindByUserAsync(userId, cancellationToken))?.Plan ?? Plan.Starter;
        var grant = await store.FindGrantAsync(userId, cancellationToken);
        if (grant is not null && grant.IsActive(time.GetUtcNow()) && grant.Plan > paid)
        {
            return new PlanGrant(grant.Plan, PlanSource.Grant, grant.EndsAt);
        }

        return paid > Plan.Starter ? new PlanGrant(paid, PlanSource.Subscription) : new PlanGrant(Plan.Starter, PlanSource.Starter);
    }

    public async Task<Plan> GetPlanAsync(Guid userId, CancellationToken cancellationToken) =>
        (await GetAccessAsync(userId, cancellationToken)).Plan;

    /// <summary>The oldest moment the user may analyse; null: the whole history.</summary>
    public async Task<DateTimeOffset?> HistoryStartAsync(Guid userId, CancellationToken cancellationToken) =>
        limits.For(await GetPlanAsync(userId, cancellationToken)).HistoryDays is { } days
            ? time.GetUtcNow().AddDays(-days)
            : null;

    /// <summary>The requested start, raised to the plan's history start. Data is never deleted, only not shown.</summary>
    public async Task<DateTimeOffset?> ClampFromAsync(Guid userId, DateTimeOffset? from, CancellationToken cancellationToken) =>
        Clamp(from, await HistoryStartAsync(userId, cancellationToken));

    public static DateTimeOffset? Clamp(DateTimeOffset? from, DateTimeOffset? start) =>
        start is null ? from : from is { } f && f > start ? f : start;
}
