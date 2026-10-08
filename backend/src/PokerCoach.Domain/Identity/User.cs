namespace PokerCoach.Domain.Identity;

/// <summary>
/// A Poker Coach account. <see cref="Id"/> is internal and immutable; external providers (Google today)
/// are linked through <see cref="ExternalIdentity"/> and the email is informational only, never a key.
/// </summary>
public sealed class User
{
    // For EF Core materialization.
    private User()
    {
    }

    private User(Guid id, string displayName, string? email, string preferredLanguage, DateTimeOffset now)
    {
        Id = id;
        DisplayName = displayName;
        Email = email;
        PreferredLanguage = preferredLanguage;
        CreatedAt = now;
        LastLoginAt = now;
        Status = UserStatus.Active;
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string PreferredLanguage { get; private set; } = UserLanguages.Default;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastLoginAt { get; private set; }

    public UserStatus Status { get; private set; }

    public static User Register(string displayName, string? email, string preferredLanguage, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        EnsureSupported(preferredLanguage);
        return new User(Guid.CreateVersion7(), displayName.Trim(), email, preferredLanguage, now);
    }

    /// <summary>Keeps the email in sync with the provider; the display name stays the user's choice.</summary>
    public void RecordLogin(DateTimeOffset now, string? email)
    {
        LastLoginAt = now;
        if (!string.IsNullOrWhiteSpace(email))
        {
            Email = email;
        }
    }

    public void ChangePreferredLanguage(string language)
    {
        EnsureSupported(language);
        PreferredLanguage = language;
    }

    private static void EnsureSupported(string language)
    {
        if (!UserLanguages.IsSupported(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language.");
        }
    }
}
