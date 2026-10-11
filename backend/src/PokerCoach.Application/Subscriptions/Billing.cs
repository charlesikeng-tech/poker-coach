using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Subscriptions;

/// <summary>Free-plan limits (ADR-0014). Configuration section "Billing:Free": a pricing test needs no release.</summary>
public sealed class FreePlanOptions
{
    public const string SectionName = "Billing:Free";

    /// <summary>Days of history a Free user sees in statistics, leaks, ranges and tournament lists.</summary>
    public int HistoryDays { get; set; } = 30;

    /// <summary>New leak explanations a Free user can generate per calendar month (UTC).</summary>
    public int MonthlyExplanations { get; set; } = 3;
}

/// <summary>Configuration section "Admin" (ADR-0015).</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Accounts whose email is listed administer the product and always have Pro. Emails reach an account only
    /// once proved (confirmed link, or verified by Google), so listing an address cannot be hijacked by
    /// someone signing up with it.
    /// </summary>
    public IList<string> Emails { get; } = [];

    public bool IsAdminEmail(string? email) =>
        email is not null && Emails.Any(e => string.Equals(EmailAddresses.Normalize(e), EmailAddresses.Normalize(email), StringComparison.Ordinal));
}

/// <summary>A grant as administrators see it.</summary>
public sealed record GrantView(Guid UserId, string DisplayName, string? Email, string Reason, DateTimeOffset? EndsAt, DateTimeOffset GrantedAt);

/// <summary>A paying (or formerly paying) customer as administrators see it.</summary>
public sealed record SubscriberView(
    Guid UserId,
    string DisplayName,
    string? Email,
    string CustomerId,
    SubscriptionStatus? Status,
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
public sealed record BillingEvent(string Id, string Type, string? CustomerId);

/// <summary>The payment provider failed or refused; the message names the provider's error, never card data.</summary>
public sealed class BillingGatewayException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Port to the payment provider (Stripe adapter in Infrastructure). Hosted pages only: no card data here.</summary>
public interface IBillingGateway
{
    /// <summary>False without a secret key: billing is off and every account has Pro (ADR-0014).</summary>
    bool IsConfigured { get; }

    /// <exception cref="BillingGatewayException"/>
    Task<string> CreateCustomerAsync(Guid userId, string? email, CancellationToken cancellationToken);

    /// <summary>A hosted checkout page for the Pro plan at that interval.</summary>
    /// <exception cref="BillingGatewayException"/>
    Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <exception cref="BillingGatewayException"/>
    Task<Uri> CreatePortalAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken);

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
    BillingInterval Interval,
    string Language,
    Uri SuccessUrl,
    Uri CancelUrl);

/// <summary>What an account has, and why.</summary>
public sealed record PlanGrant(Plan Plan, PlanSource Source, DateTimeOffset? GrantEndsAt = null);

/// <summary>
/// Which plan a user has and what it opens (ADR-0014, ADR-0015). In order: administrators, then billing off
/// (Pro for everyone: development, tests, self-hosting), then a live Stripe subscription, then a grant.
/// </summary>
public sealed class PlanAccess(
    ISubscriptionStore store,
    IBillingGateway gateway,
    IUserAccountStore users,
    AdminOptions admins,
    FreePlanOptions free,
    TimeProvider time)
{
    public FreePlanOptions FreeLimits => free;

    public bool BillingEnabled => gateway.IsConfigured;

    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        admins.Emails.Count > 0 && admins.IsAdminEmail((await users.FindByIdAsync(userId, cancellationToken))?.Email);

    public async Task<PlanGrant> GetAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await IsAdminAsync(userId, cancellationToken))
        {
            return new PlanGrant(Plan.Pro, PlanSource.Admin);
        }

        if (!gateway.IsConfigured)
        {
            return new PlanGrant(Plan.Pro, PlanSource.BillingOff);
        }

        if ((await store.FindByUserAsync(userId, cancellationToken))?.Plan == Plan.Pro)
        {
            return new PlanGrant(Plan.Pro, PlanSource.Subscription);
        }

        return await store.FindGrantAsync(userId, cancellationToken) is { } grant && grant.IsActive(time.GetUtcNow())
            ? new PlanGrant(Plan.Pro, PlanSource.Grant, grant.EndsAt)
            : new PlanGrant(Plan.Free, PlanSource.Free);
    }

    public async Task<Plan> GetPlanAsync(Guid userId, CancellationToken cancellationToken) =>
        (await GetAccessAsync(userId, cancellationToken)).Plan;

    /// <summary>The oldest moment the user may analyse; null: the whole history.</summary>
    public async Task<DateTimeOffset?> HistoryStartAsync(Guid userId, CancellationToken cancellationToken) =>
        await GetPlanAsync(userId, cancellationToken) == Plan.Pro
            ? null
            : time.GetUtcNow().AddDays(-free.HistoryDays);

    /// <summary>The requested start, raised to the plan's history start. Data is never deleted, only not shown.</summary>
    public async Task<DateTimeOffset?> ClampFromAsync(Guid userId, DateTimeOffset? from, CancellationToken cancellationToken) =>
        Clamp(from, await HistoryStartAsync(userId, cancellationToken));

    public static DateTimeOffset? Clamp(DateTimeOffset? from, DateTimeOffset? start) =>
        start is null ? from : from is { } f && f > start ? f : start;
}
