using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Subscriptions;

public enum GrantResult
{
    Granted,
    UserNotFound,
    InvalidReason,
    EndInPast,
}

/// <summary>
/// What administrators do in the app (ADR-0015): give or take back Pro without payment, and see who pays.
/// Everything else about billing (refunds, cancellations, coupons) is done in the Stripe dashboard.
/// The caller checks <see cref="PlanAccess.IsAdminAsync"/> first.
/// </summary>
public sealed class AdminService(ISubscriptionStore store, ILocalAccountStore accounts, TimeProvider time)
{
    /// <summary>Subscribers listed at most: enough for the product's size; Stripe has the full list.</summary>
    public const int MaxSubscribers = 500;

    public Task<IReadOnlyList<GrantView>> ListGrantsAsync(CancellationToken cancellationToken) =>
        store.ListGrantsAsync(cancellationToken);

    public Task<IReadOnlyList<SubscriberView>> ListSubscribersAsync(CancellationToken cancellationToken) =>
        store.ListSubscribersAsync(MaxSubscribers, cancellationToken);

    /// <param name="endsAt">Null: until revoked.</param>
    public async Task<GrantResult> GrantAsync(Guid adminId, string email, string reason, DateTimeOffset? endsAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return GrantResult.InvalidReason;
        }

        var now = time.GetUtcNow();
        if (endsAt is { } end && end <= now)
        {
            return GrantResult.EndInPast;
        }

        if (!EmailAddresses.IsValid(email)
            || await accounts.FindUserByEmailAsync(EmailAddresses.Normalize(email), cancellationToken) is not { } user)
        {
            return GrantResult.UserNotFound;
        }

        await store.SaveGrantAsync(ProGrant.Give(user.Id, reason, endsAt, adminId, now), cancellationToken);
        return GrantResult.Granted;
    }

    public Task<bool> RevokeAsync(Guid userId, CancellationToken cancellationToken) =>
        store.DeleteGrantAsync(userId, cancellationToken);
}
