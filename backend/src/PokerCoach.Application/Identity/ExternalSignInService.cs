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
/// on (provider, subject). A first Google sign-in with the email of a confirmed email-and-password account
/// joins that account (ADR-0013): both prove the same mailbox. An unconfirmed one proves nothing and is
/// not joined.
/// </summary>
public sealed class ExternalSignInService(IUserAccountStore store, TimeProvider timeProvider, ILocalAccountStore? localAccounts = null)
{
    public async Task<User> SignInAsync(ExternalSignIn signIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        var now = timeProvider.GetUtcNow();

        var user = await store.FindByExternalIdentityAsync(signIn.Provider, signIn.ProviderSubjectId, cancellationToken)
            ?? await JoinConfirmedLocalAccountAsync(signIn, now, cancellationToken);
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

    private async Task<User?> JoinConfirmedLocalAccountAsync(ExternalSignIn signIn, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (localAccounts is null || !EmailAddresses.IsValid(signIn.Email))
        {
            return null;
        }

        var credential = await localAccounts.FindCredentialByEmailAsync(EmailAddresses.Normalize(signIn.Email!), cancellationToken);
        if (credential is not { IsConfirmed: true })
        {
            return null;
        }

        var user = await store.FindByIdAsync(credential.UserId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        // Losing a race against a parallel first sign-in leaves the identity linked anyway.
        await store.TryLinkAsync(ExternalIdentity.Link(user, signIn.Provider, signIn.ProviderSubjectId, signIn.Email, now), cancellationToken);
        return await store.FindByExternalIdentityAsync(signIn.Provider, signIn.ProviderSubjectId, cancellationToken) ?? user;
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
