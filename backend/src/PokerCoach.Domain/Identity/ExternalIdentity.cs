namespace PokerCoach.Domain.Identity;

/// <summary>
/// Link between a <see cref="User"/> and an account at an external identity provider.
/// (<see cref="Provider"/>, <see cref="ProviderSubjectId"/>) is unique: that pair is what identifies the
/// person, because emails can change hands.
/// </summary>
public sealed class ExternalIdentity
{
    // For EF Core materialization.
    private ExternalIdentity()
    {
    }

    private ExternalIdentity(Guid id, Guid userId, string provider, string providerSubjectId, string? email, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        Provider = provider;
        ProviderSubjectId = providerSubjectId;
        Email = email;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderSubjectId { get; private set; } = string.Empty;

    /// <summary>Email at link time, kept for support purposes.</summary>
    public string? Email { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ExternalIdentity Link(User user, string provider, string providerSubjectId, string? email, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubjectId);
        return new ExternalIdentity(Guid.CreateVersion7(), user.Id, provider, providerSubjectId, email, now);
    }
}

public static class IdentityProviders
{
    public const string Google = "google";
}
