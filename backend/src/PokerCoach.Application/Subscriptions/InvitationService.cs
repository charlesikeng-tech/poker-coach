using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Subscriptions;

public enum InvitationResult
{
    Sent,
    InvalidEmail,
    InvalidPlan,
    InvalidReason,
    EndInPast,
    AlreadyRegistered,
}

/// <summary>
/// Invitations (admin-only): invite someone who does not have an account yet. When they sign up, a
/// <see cref="ProGrant"/> is created automatically. The invitation email is sent via Brevo.
/// </summary>
public sealed class InvitationService(
    IInvitationStore invitations,
    ISubscriptionStore subscriptions,
    ILocalAccountStore localAccounts,
    IEmailSender emailSender,
    TimeProvider time)
{
    /// <summary>Default invitation validity.</summary>
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromDays(7);

    public async Task<InvitationResult> InviteAsync(
        Guid adminId,
        string email,
        Plan plan,
        string reason,
        DateTimeOffset? grantEndsAt,
        TimeSpan? expiresIn,
        string signUpUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return InvitationResult.InvalidReason;
        }

        if (plan == Plan.Starter)
        {
            return InvitationResult.InvalidPlan;
        }

        if (!EmailAddresses.IsValid(email))
        {
            return InvitationResult.InvalidEmail;
        }

        var normalized = EmailAddresses.Normalize(email);
        var now = time.GetUtcNow();

        if (grantEndsAt is { } end && end <= now)
        {
            return InvitationResult.EndInPast;
        }

        // If the email already has an account, use Grant instead.
        if (await localAccounts.FindUserByEmailAsync(normalized, cancellationToken) is not null)
        {
            return InvitationResult.AlreadyRegistered;
        }

        var invitation = Invitation.Create(normalized, plan, reason, grantEndsAt, adminId, now, expiresIn ?? DefaultExpiry);
        await invitations.SaveAsync(invitation, cancellationToken);

        var language = "fr"; // Admin emails default to French; the invitee picks their language on sign-up.
        await emailSender.SendAsync(InvitationEmails.Invite(normalized, language, plan, signUpUrl), cancellationToken);

        return InvitationResult.Sent;
    }

    public Task<IReadOnlyList<InvitationView>> ListAsync(CancellationToken cancellationToken) =>
        invitations.ListAsync(cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        invitations.DeleteAsync(id, cancellationToken);

    /// <summary>
    /// Called after a new account is created (local confirmation or external sign-in): if an active invitation
    /// matches the email, creates the <see cref="ProGrant"/> and marks the invitation as accepted.
    /// </summary>
    public async Task ClaimAsync(Guid userId, string? email, CancellationToken cancellationToken)
    {
        if (email is null || !EmailAddresses.IsValid(email))
        {
            return;
        }

        var normalized = EmailAddresses.Normalize(email);
        var now = time.GetUtcNow();
        var invitation = await invitations.FindActiveByEmailAsync(normalized, now, cancellationToken);
        if (invitation is null)
        {
            return;
        }

        invitation.Accept(now);
        var grant = ProGrant.Give(userId, invitation.Plan, invitation.Reason, invitation.GrantEndsAt, invitation.InvitedBy, now);
        await subscriptions.SaveGrantAsync(grant, cancellationToken);
        await invitations.SaveChangesAsync(cancellationToken);
    }
}
