namespace PokerCoach.Domain.Subscriptions;

public enum Plan
{
    Free = 0,
    Pro = 1,
}

public enum BillingInterval
{
    Month = 0,
    Year = 1,
}

/// <summary>Stripe's subscription statuses, as Stripe names them.</summary>
public enum SubscriptionStatus
{
    Incomplete = 0,
    IncompleteExpired = 1,
    Trialing = 2,
    Active = 3,
    PastDue = 4,
    Canceled = 5,
    Unpaid = 6,
    Paused = 7,
}

/// <summary>What the payment provider says about a customer's current subscription.</summary>
/// <param name="CurrentPeriodEnd">Renewal date, or the end date when <paramref name="CancelAtPeriodEnd"/>.</param>
public sealed record SubscriptionState(
    string SubscriptionId,
    SubscriptionStatus Status,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

/// <summary>
/// A user's billing record (ADR-0014): their Stripe customer and the last known state of their subscription.
/// The plan is derived from the status, never stored, so it cannot drift from Stripe.
/// </summary>
public sealed class Subscription
{
    // For EF Core materialization.
    private Subscription()
    {
    }

    private Subscription(Guid userId, string customerId, DateTimeOffset now)
    {
        UserId = userId;
        CustomerId = customerId;
        CreatedAt = now;
        SyncedAt = now;
    }

    public Guid UserId { get; private set; }

    public string CustomerId { get; private set; } = string.Empty;

    public string? SubscriptionId { get; private set; }

    /// <summary>Null until the customer has subscribed once.</summary>
    public SubscriptionStatus? Status { get; private set; }

    public BillingInterval? Interval { get; private set; }

    public DateTimeOffset? CurrentPeriodEnd { get; private set; }

    public bool CancelAtPeriodEnd { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    /// <summary>
    /// Pro while Stripe considers the subscription alive, including <c>past_due</c>: Stripe is retrying the
    /// card, and cutting a paying user on the first failed charge would be hostile.
    /// </summary>
    public Plan Plan => Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue
        ? Plan.Pro
        : Plan.Free;

    public static Subscription Open(Guid userId, string customerId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return new Subscription(userId, customerId, now);
    }

    /// <summary>Replaces the known state with Stripe's; null: the customer has no subscription any more.</summary>
    public void Sync(SubscriptionState? state, DateTimeOffset now)
    {
        SubscriptionId = state?.SubscriptionId;
        Status = state?.Status;
        Interval = state?.Interval;
        CurrentPeriodEnd = state?.CurrentPeriodEnd;
        CancelAtPeriodEnd = state?.CancelAtPeriodEnd ?? false;
        SyncedAt = now;
    }

    /// <summary>
    /// Of several subscriptions of one customer (a past one canceled, a new one active), the one that decides
    /// the plan: an alive one first, then the most recent period end.
    /// </summary>
    public static SubscriptionState? Current(IEnumerable<SubscriptionState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        return states
            .OrderByDescending(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue)
            .ThenByDescending(s => s.CurrentPeriodEnd ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }
}
