using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Identity;

/// <summary>What the identity provider told us about the person signing in.</summary>
/// <param name="PreferredLanguage">Language the person was using before signing in; ignored if unsupported.</param>
public sealed record ExternalSignIn(
    string Provider,
    string ProviderSubjectId,
    string? Email,
    string? DisplayName,
    string? PreferredLanguage);

/// <summary>
/// Finds or creates the account behind an external sign-in. Safe under concurrency: two simultaneous
/// first sign-ins of the same person end with one account, guarded by the database unique constraint
/// on (provider, subject).
/// </summary>
public sealed class ExternalSignInService(IUserAccountStore store, TimeProvider timeProvider)
{
    public async Task<User> SignInAsync(ExternalSignIn signIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        var now = timeProvider.GetUtcNow();

        var user = await store.FindByExternalIdentityAsync(signIn.Provider, signIn.ProviderSubjectId, cancellationToken);
        if (user is null)
        {
            var newUser = User.Register(
                DisplayNameOf(signIn),
                signIn.Email,
                UserLanguages.IsSupported(signIn.PreferredLanguage) ? signIn.PreferredLanguage! : UserLanguages.Default,
                now);
            var identity = ExternalIdentity.Link(newUser, signIn.Provider, signIn.ProviderSubjectId, signIn.Email, now);

            if (await store.TryAddAsync(newUser, identity, cancellationToken))
            {
                return newUser;
            }

            // Lost the race against a parallel first sign-in: use the account it created.
            user = await store.FindByExternalIdentityAsync(signIn.Provider, signIn.ProviderSubjectId, cancellationToken)
                ?? throw new InvalidOperationException("External identity reported as existing but not found.");
        }

        user.RecordLogin(now, signIn.Email);
        await store.SaveChangesAsync(cancellationToken);
        return user;
    }

    // Providers do not always send a name; never store an empty display name.
    private static string DisplayNameOf(ExternalSignIn signIn)
    {
        if (!string.IsNullOrWhiteSpace(signIn.DisplayName))
        {
            return signIn.DisplayName;
        }

        var email = signIn.Email;
        var at = email?.IndexOf('@', StringComparison.Ordinal) ?? -1;
        return at > 0 ? email![..at] : "Player";
    }
}
