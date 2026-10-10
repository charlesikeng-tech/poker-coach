using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Tests.Identity;

/// <summary>
/// Test double with the same contract as the database store, including the unique (provider, subject)
/// rule. <see cref="SimulateConcurrentFirstSignIn"/> makes the next insert lose a race.
/// </summary>
internal sealed class InMemoryUserAccountStore : IUserAccountStore
{
    private readonly List<User> users = [];
    private readonly List<ExternalIdentity> identities = [];

    public int SaveCount { get; private set; }

    public IReadOnlyList<User> Users => users;

    /// <summary>Account created "by another request" just before the next <see cref="TryAddAsync"/>.</summary>
    public (User User, ExternalIdentity Identity)? SimulateConcurrentFirstSignIn { get; set; }

    public Task<User?> FindByExternalIdentityAsync(string provider, string providerSubjectId, CancellationToken cancellationToken)
    {
        var identity = identities.Find(i => i.Provider == provider && i.ProviderSubjectId == providerSubjectId);
        return Task.FromResult(identity is null ? null : users.Find(u => u.Id == identity.UserId));
    }

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(users.Find(u => u.Id == userId));

    public Task<bool> TryAddAsync(User user, ExternalIdentity identity, CancellationToken cancellationToken)
    {
        if (SimulateConcurrentFirstSignIn is { } concurrent)
        {
            SimulateConcurrentFirstSignIn = null;
            users.Add(concurrent.User);
            identities.Add(concurrent.Identity);
        }

        if (identities.Exists(i => i.Provider == identity.Provider && i.ProviderSubjectId == identity.ProviderSubjectId))
        {
            return Task.FromResult(false);
        }

        users.Add(user);
        identities.Add(identity);
        SaveCount++;
        return Task.FromResult(true);
    }

    public Task<bool> TryLinkAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        if (identities.Exists(i => i.Provider == identity.Provider && i.ProviderSubjectId == identity.ProviderSubjectId))
        {
            return Task.FromResult(false);
        }

        identities.Add(identity);
        return Task.FromResult(true);
    }

    /// <summary>For the local-account fake, which shares this store's users.</summary>
    public void AddUser(User user) => users.Add(user);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
