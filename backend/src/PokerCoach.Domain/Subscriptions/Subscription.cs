namespace PokerCoach.Domain.Subscriptions;

/// <summary>The offers (ADR-0016), in increasing order: a plan opens everything the ones below it open.</summary>
public enum Plan
{
    /// <summary>The free offer.</summary>
    Starter = 0,
    Pro = 1,
    Elite = 2,
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
/// <param name="Tier">The paid offer its price belongs to; null for a price we do not know.</param>
public sealed record SubscriptionState(
    string SubscriptionId,
    SubscriptionStatus Status,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    Plan? Tier = null);

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

    /// <summary>The paid offer subscribed to (from the Stripe price); null when unknown.</summary>
    public Plan? Tier { get; private set; }

    public DateTimeOffset? CurrentPeriodEnd { get; private set; }

    public bool CancelAtPeriodEnd { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset SyncedAt { get; private set; }

    public bool IsAlive => Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue;

    /// <summary>
    /// The subscribed offer while Stripe considers the subscription alive, including <c>past_due</c>: Stripe is
    /// retrying the card, and cutting a paying user on the first failed charge would be hostile. A price we do
    /// not know counts as Pro: never more than the lowest paid offer on a guess.
    /// </summary>
    public Plan Plan => IsAlive ? Tier ?? Plan.Pro : Plan.Starter;

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
        Tier = state?.Tier;
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

/// <summary>
/// A paid offer given without payment (ADR-0015, ADR-0016): partners during the test phase, friends, support gestures. One per
/// user; granting again replaces it, revoking deletes it. Without an end date it lasts until revoked.
/// </summary>
public sealed class ProGrant
{
    public const int ReasonMaxLength = 200;

    // For EF Core materialization.
    private ProGrant()
    {
    }

    private ProGrant(Guid userId, Plan plan, string reason, DateTimeOffset? endsAt, Guid grantedBy, DateTimeOffset now)
    {
        UserId = userId;
        Plan = plan;
        Reason = reason;
        EndsAt = endsAt;
        GrantedBy = grantedBy;
        GrantedAt = now;
    }

    public Guid UserId { get; private set; }

    /// <summary>The offer given: Pro or Elite.</summary>
    public Plan Plan { get; private set; } = Plan.Pro;

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>The administrator who granted it.</summary>
    public Guid GrantedBy { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => EndsAt is null || EndsAt > now;

    public static ProGrant Give(Guid userId, Plan plan, string reason, DateTimeOffset? endsAt, Guid grantedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (plan == Plan.Starter)
        {
            throw new ArgumentOutOfRangeException(nameof(plan), "Only a paid offer can be given.");
        }

        if (endsAt is { } end && end <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAt), "A grant ends in the future.");
        }

        var trimmed = reason.Trim();
        return new ProGrant(userId, plan, trimmed.Length > ReasonMaxLength ? trimmed[..ReasonMaxLength] : trimmed, endsAt, grantedBy, now);
    }

    /// <summary>Granting again: new reason and end, new author.</summary>
    public void Renew(ProGrant replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        Plan = replacement.Plan;
        Reason = replacement.Reason;
        EndsAt = replacement.EndsAt;
        GrantedBy = replacement.GrantedBy;
        GrantedAt = replacement.GrantedAt;
    }
}

/// <summary>Why an account has the plan it has; shown to the user and to administrators.</summary>
public enum PlanSource
{
    /// <summary>Billing is off on this server: everyone has Pro.</summary>
    BillingOff = 0,
    Starter = 1,
    Subscription = 2,
    Grant = 3,
    Admin = 4,
}

/// <summary>What the offers open besides history and AI credits (ADR-0016).</summary>
public enum Feature
{
    /// <summary>Opening ranges against the references.</summary>
    OpeningRanges,

    /// <summary>Push/fold equilibrium and the hand-by-hand gap in the ranges.</summary>
    PushFoldRanges,

    /// <summary>Postflop leaks, statistics by phase and stack, luck at the all-ins.</summary>
    AdvancedLeaks,

    /// <summary>Generic drills (open, push or fold, big blind defence).</summary>
    TrainingDrills,

    /// <summary>Quiz on the player's own missed spots, with spaced repetition.</summary>
    TrainingOwnHands,

    /// <summary>The coach's review of a week: once a month in Pro, every week in Elite.</summary>
    WeekReview,

    /// <summary>The coach's debrief of a tournament.</summary>
    TournamentDebrief,
}

/// <summary>The offer matrix (ADR-0016): the lowest plan that opens each feature.</summary>
public static class PlanFeatures
{
    public static Plan MinimumPlan(Feature feature) => feature switch
    {
        Feature.OpeningRanges or Feature.TrainingDrills or Feature.WeekReview => Plan.Pro,
        _ => Plan.Elite,
    };

    public static bool Allows(this Plan plan, Feature feature) => plan >= MinimumPlan(feature);
}
