using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Identity;

public sealed record UserProfile(Guid Id, string DisplayName, string? Email, string PreferredLanguage);

public sealed class UserProfileService(IUserAccountStore store)
{
    public async Task<UserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindByIdAsync(userId, cancellationToken);
        return user is null ? null : new UserProfile(user.Id, user.DisplayName, user.Email, user.PreferredLanguage);
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
