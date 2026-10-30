namespace PokerCoach.Domain.Subscriptions;

/// <summary>
/// An invitation sent by an administrator to someone who does not have an account yet. When the invitee
/// signs up (email-and-password or Google), the system creates a <see cref="ProGrant"/> automatically and
/// marks the invitation as accepted. One active invitation per email; inviting again replaces the previous one.
/// </summary>
public sealed class Invitation
{
    public const int ReasonMaxLength = 200;
    public const int EmailMaxLength = 320;

    // For EF Core materialization.
    private Invitation()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The normalized email of the person to invite.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>The plan the invitee will receive on sign-up.</summary>
    public Plan Plan { get; private set; }

    /// <summary>Why the grant is given (passed to <see cref="ProGrant"/>).</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>When the resulting grant should end; null: until revoked.</summary>
    public DateTimeOffset? GrantEndsAt { get; private set; }

    /// <summary>The administrator who sent the invitation.</summary>
    public Guid InvitedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the invitation can no longer be accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Non-null once the invitee has created an account and the grant was applied.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => AcceptedAt is null && ExpiresAt > now;

    public void Accept(DateTimeOffset now) => AcceptedAt = now;

    public static Invitation Create(
        string normalizedEmail,
        Plan plan,
        string reason,
        DateTimeOffset? grantEndsAt,
        Guid invitedBy,
        DateTimeOffset now,
        TimeSpan expiresIn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (plan == Plan.Starter)
        {
            throw new ArgumentOutOfRangeException(nameof(plan), "Only a paid offer can be invited.");
        }

        if (grantEndsAt is { } end && end <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(grantEndsAt), "A grant ends in the future.");
        }

        var trimmed = reason.Trim();
        return new Invitation
        {
            Id = Guid.CreateVersion7(),
            Email = normalizedEmail,
            Plan = plan,
            Reason = trimmed.Length > ReasonMaxLength ? trimmed[..ReasonMaxLength] : trimmed,
            GrantEndsAt = grantEndsAt,
            InvitedBy = invitedBy,
            CreatedAt = now,
            ExpiresAt = now + expiresIn,
        };
    }
}
