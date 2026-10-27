using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Identity;

public sealed record UserProfile(Guid Id, string DisplayName, string? Email, string PreferredLanguage, StakesLevel StakesLevel, bool HasPassword, IReadOnlyList<string> LinkedProviders);

public sealed class UserProfileService(IUserAccountStore store, ILocalAccountStore? localAccounts = null)
{
    public async Task<UserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var providers = await store.GetLinkedProvidersAsync(userId, cancellationToken);
        var hasPassword = localAccounts is not null
            && await localAccounts.FindCredentialByUserAsync(userId, cancellationToken) is not null;

        return new UserProfile(user.Id, user.DisplayName, user.Email, user.PreferredLanguage, user.StakesLevel, hasPassword, providers);
    }

    /// <returns>False when the user does not exist.</returns>
    public async Task<bool> ChangeDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken)
    {
        var user = await store.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.ChangeDisplayName(displayName);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <returns>False when the user does not exist.</returns>
    public async Task<bool> ChangeStakesLevelAsync(Guid userId, StakesLevel level, CancellationToken cancellationToken)
    {
        var user = await store.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.ChangeStakesLevel(level);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <returns>False when the user does not exist.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Unsupported language: validate input first.</exception>
    public async Task<bool> ChangePreferredLanguageAsync(Guid userId, string language, CancellationToken cancellationToken)
    {
        var user = await store.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.ChangePreferredLanguage(language);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }
}
