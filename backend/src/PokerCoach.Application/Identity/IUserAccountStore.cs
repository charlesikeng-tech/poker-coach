using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Identity;

/// <summary>Persistence port for user accounts (implemented in Infrastructure).</summary>
public interface IUserAccountStore
{
    Task<User?> FindByExternalIdentityAsync(string provider, string providerSubjectId, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a new user and its first external identity atomically.
    /// Returns false, saving nothing, when the identity already exists (concurrent first sign-in).
    /// </summary>
    Task<bool> TryAddAsync(User user, ExternalIdentity identity, CancellationToken cancellationToken);

    /// <summary>Persists changes made to users loaded through this store.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
