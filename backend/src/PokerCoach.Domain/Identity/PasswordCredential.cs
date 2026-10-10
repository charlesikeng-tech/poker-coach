using System.Net.Mail;

namespace PokerCoach.Domain.Identity;

/// <summary>
/// Email and password sign-in for a <see cref="User"/> (ADR-0013). The email is the login, unique among
/// credentials; it is usable only once the person has proved they own the mailbox
/// (<see cref="EmailConfirmedAt"/>). The hash is produced and checked outside the domain.
/// </summary>
public sealed class PasswordCredential
{
    /// <summary>Failed attempts in a row before the account is locked for <see cref="LockoutDuration"/>.</summary>
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // For EF Core materialization.
    private PasswordCredential()
    {
    }

    private PasswordCredential(Guid userId, string email, string passwordHash, DateTimeOffset now)
    {
        UserId = userId;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = now;
        PasswordChangedAt = now;
    }

    public Guid UserId { get; private set; }

    /// <summary>Normalized (<see cref="EmailAddresses.Normalize"/>).</summary>
    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset? EmailConfirmedAt { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset PasswordChangedAt { get; private set; }

    public bool IsConfirmed => EmailConfirmedAt is not null;

    public static PasswordCredential Create(User user, string normalizedEmail, string passwordHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        return new PasswordCredential(user.Id, normalizedEmail, passwordHash, now);
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>Counts a wrong password; the fifth in a row locks the account for a while.</summary>
    public void RecordFailure(DateTimeOffset now)
    {
        FailedAttempts++;
        if (FailedAttempts >= MaxFailedAttempts)
        {
            LockedUntil = now + LockoutDuration;
            FailedAttempts = 0;
        }
    }

    public void RecordSuccess()
    {
        FailedAttempts = 0;
        LockedUntil = null;
    }

    public void ConfirmEmail(DateTimeOffset now) => EmailConfirmedAt ??= now;

    /// <summary>A new password also ends any lockout: whoever sets it proved they own the mailbox or the old password.</summary>
    public void ChangePassword(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        PasswordChangedAt = now;
        RecordSuccess();
    }

    /// <summary>Same password, stronger hash parameters (the hasher asked for it after a successful check).</summary>
    public void Rehash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }
}

/// <summary>Email handling shared by sign-up, sign-in and recovery: one normalized form, one validity rule.</summary>
public static class EmailAddresses
{
    public const int MaxLength = 254;

    /// <summary>Trimmed and lower-cased: "Jo@Example.com " and "jo@example.com" are the same login.</summary>
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    /// <summary>A plain address with a dotted domain; display names ("Jo &lt;jo@x.fr&gt;") are refused.</summary>
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > MaxLength)
        {
            return false;
        }

        var trimmed = email.Trim();
        return MailAddress.TryCreate(trimmed, out var address)
            && address.Address == trimmed
            && address.Host.Contains('.', StringComparison.Ordinal)
            && !address.Host.EndsWith('.');
    }
}

public enum EmailTokenPurpose
{
    ConfirmEmail = 0,
    ResetPassword = 1,
}

/// <summary>
/// A single-use link sent by email. Only the SHA-256 of the token is stored: a database leak does not hand
/// out working links.
/// </summary>
public sealed class EmailToken
{
    // For EF Core materialization.
    private EmailToken()
    {
    }

    private EmailToken(Guid id, Guid userId, EmailTokenPurpose purpose, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public EmailTokenPurpose Purpose { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Set atomically by the store when the link is used (single use under concurrency).</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public static TimeSpan Lifetime(EmailTokenPurpose purpose) =>
        purpose == EmailTokenPurpose.ResetPassword ? TimeSpan.FromHours(1) : TimeSpan.FromHours(24);

    public static EmailToken Issue(Guid userId, EmailTokenPurpose purpose, string tokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new EmailToken(Guid.CreateVersion7(now), userId, purpose, tokenHash, now, now + Lifetime(purpose));
    }
}
