using System.Security.Cryptography;
using System.Text;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Identity;

/// <summary>Persistence port for email and password accounts (ADR-0013).</summary>
public interface ILocalAccountStore
{
    /// <summary>Tracked: changes are saved by <see cref="SaveChangesAsync"/>.</summary>
    Task<PasswordCredential?> FindCredentialByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<PasswordCredential?> FindCredentialByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>An existing account whose email (from Google, say) is this one, case-insensitively.</summary>
    Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A new user and its credential in one transaction; false, saving nothing, when the email is taken.</summary>
    Task<bool> TryAddAsync(User user, PasswordCredential credential, CancellationToken cancellationToken);

    /// <summary>A credential for an existing user (a Google account adding a password); false when the email is taken.</summary>
    Task<bool> TryAddCredentialAsync(PasswordCredential credential, CancellationToken cancellationToken);

    Task AddTokenAsync(EmailToken token, CancellationToken cancellationToken);

    Task<int> CountTokensSinceAsync(Guid userId, EmailTokenPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>The token's id and user if it exists, is unused and not expired.</summary>
    Task<(Guid TokenId, Guid UserId)?> FindUsableTokenAsync(string tokenHash, EmailTokenPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Marks the token used, atomically: of two concurrent uses, one gets true.</summary>
    Task<bool> TryConsumeTokenAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Invalidates the user's unused tokens of this purpose (a new password voids older reset links).</summary>
    Task RevokeTokensAsync(Guid userId, EmailTokenPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>Right password, hashed with parameters weaker than today's: hash it again.</summary>
    SuccessRehashNeeded,
}

/// <summary>Password hashing (PBKDF2 in Infrastructure). Never reversible, never logged.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string hash, string password);
}

public sealed record EmailMessage(string To, string Subject, string Text, string Html);

/// <summary>Sends transactional emails (Brevo in production, the log in development).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Where the links in emails point: the web app's public address, never the request's Host header.</summary>
public sealed record AccountLinks(string BaseUrl)
{
    public string ConfirmEmail(string token) => $"{BaseUrl.TrimEnd('/')}/confirm-email?token={Uri.EscapeDataString(token)}";

    public string ResetPassword(string token) => $"{BaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";

    public string SignIn() => $"{BaseUrl.TrimEnd('/')}/sign-in";
}

public enum RegistrationResult
{
    /// <summary>Same answer whether the email was new or not: nobody learns who has an account.</summary>
    Accepted,
    InvalidEmail,
    WeakPassword,
}

public enum LocalSignInFailure
{
    InvalidCredentials,
    EmailNotConfirmed,
    Locked,
    InvalidToken,
    WeakPassword,
}

public sealed record LocalSignInOutcome(User? User, LocalSignInFailure? Failure)
{
    public static LocalSignInOutcome Of(User user) => new(user, null);

    public static LocalSignInOutcome Fail(LocalSignInFailure failure) => new(null, failure);
}

/// <summary>
/// Email and password accounts (ADR-0013): sign-up with a confirmation link, sign-in with lockout,
/// forgotten password. Rules that matter for security:
/// <list type="bullet">
/// <item>Answers never tell whether an email has an account (sign-up, resend, forgotten password).</item>
/// <item>Confirming an address requires the password too: someone who signs up with another person's email
/// cannot get that person to activate the account by clicking a link they never asked for.</item>
/// <item>An email already used by an account (Google) never gets a password through sign-up; its owner adds
/// one through the forgotten-password link, which proves they own the mailbox.</item>
/// </list>
/// </summary>
public sealed class LocalAccountService(
    ILocalAccountStore store,
    IPasswordHasher hasher,
    IEmailSender email,
    TimeProvider time)
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;

    /// <summary>Emails of one kind per account and hour: enough for a typo, not for flooding someone's inbox.</summary>
    public const int MaxEmailsPerHour = 3;

    // One per process: hashing costs ~50 ms, it is only there to make unknown emails as slow as known ones.
    private static string? dummyHash;

    public async Task<RegistrationResult> RegisterAsync(
        string emailAddress,
        string password,
        string? displayName,
        string language,
        AccountLinks links,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (!EmailAddresses.IsValid(emailAddress))
        {
            return RegistrationResult.InvalidEmail;
        }

        var normalized = EmailAddresses.Normalize(emailAddress);
        if (!IsStrongEnough(password, normalized))
        {
            return RegistrationResult.WeakPassword;
        }

        var answerLanguage = UserLanguages.IsSupported(language) ? language : UserLanguages.Default;
        var now = time.GetUtcNow();

        var credential = await store.FindCredentialByEmailAsync(normalized, cancellationToken);
        if (credential is not null)
        {
            if (credential.IsConfirmed)
            {
                await SendAlreadyRegisteredAsync(credential.UserId, normalized, answerLanguage, links, cancellationToken);
            }
            else
            {
                // Never confirmed: the latest sign-up sets the password, and confirming will require it.
                credential.ChangePassword(hasher.Hash(password), now);
                await store.RevokeTokensAsync(credential.UserId, EmailTokenPurpose.ConfirmEmail, now, cancellationToken);
                await store.SaveChangesAsync(cancellationToken);
                await SendConfirmationAsync(credential.UserId, normalized, answerLanguage, links, cancellationToken);
            }

            return RegistrationResult.Accepted;
        }

        var existing = await store.FindUserByEmailAsync(normalized, cancellationToken);
        if (existing is not null)
        {
            await SendAlreadyRegisteredAsync(existing.Id, normalized, existing.PreferredLanguage, links, cancellationToken);
            return RegistrationResult.Accepted;
        }

        var user = User.Register(DisplayNameOf(displayName, normalized), normalized, answerLanguage, now);
        var created = PasswordCredential.Create(user, normalized, hasher.Hash(password), now);
        if (await store.TryAddAsync(user, created, cancellationToken))
        {
            await SendConfirmationAsync(user.Id, normalized, answerLanguage, links, cancellationToken);
        }

        // Lost a race against the same sign-up: the first one sent the email.
        return RegistrationResult.Accepted;
    }

    public async Task<LocalSignInOutcome> SignInAsync(string emailAddress, string password, CancellationToken cancellationToken)
    {
        var credential = EmailAddresses.IsValid(emailAddress)
            ? await store.FindCredentialByEmailAsync(EmailAddresses.Normalize(emailAddress), cancellationToken)
            : null;
        if (credential is null)
        {
            // Same work as a real check: response time does not tell which emails have an account.
            hasher.Verify(DummyHash(), password ?? string.Empty);
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidCredentials);
        }

        var now = time.GetUtcNow();
        if (credential.IsLocked(now))
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.Locked);
        }

        var check = hasher.Verify(credential.PasswordHash, password ?? string.Empty);
        if (check == PasswordCheck.Failed)
        {
            credential.RecordFailure(now);
            await store.SaveChangesAsync(cancellationToken);
            return LocalSignInOutcome.Fail(credential.IsLocked(now) ? LocalSignInFailure.Locked : LocalSignInFailure.InvalidCredentials);
        }

        // Only someone who knows the password learns that the address is not confirmed yet.
        if (!credential.IsConfirmed)
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.EmailNotConfirmed);
        }

        return await CompleteSignInAsync(credential, check, password!, now, cancellationToken);
    }

    /// <summary>The link from the confirmation email, with the password chosen at sign-up.</summary>
    public async Task<LocalSignInOutcome> ConfirmEmailAsync(string token, string password, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var found = await store.FindUsableTokenAsync(Hash(token), EmailTokenPurpose.ConfirmEmail, now, cancellationToken);
        if (found is not { } usable)
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidToken);
        }

        var credential = await store.FindCredentialByUserAsync(usable.UserId, cancellationToken);
        if (credential is null)
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidToken);
        }

        if (credential.IsLocked(now))
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.Locked);
        }

        var check = hasher.Verify(credential.PasswordHash, password ?? string.Empty);
        if (check == PasswordCheck.Failed)
        {
            credential.RecordFailure(now);
            await store.SaveChangesAsync(cancellationToken);
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidCredentials);
        }

        if (!await store.TryConsumeTokenAsync(usable.TokenId, now, cancellationToken))
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidToken);
        }

        credential.ConfirmEmail(now);
        return await CompleteSignInAsync(credential, check, password!, now, cancellationToken);
    }

    /// <summary>Sends a new confirmation link to an unconfirmed address; silent otherwise.</summary>
    public async Task ResendConfirmationAsync(string emailAddress, string language, AccountLinks links, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (!EmailAddresses.IsValid(emailAddress))
        {
            return;
        }

        var normalized = EmailAddresses.Normalize(emailAddress);
        var credential = await store.FindCredentialByEmailAsync(normalized, cancellationToken);
        if (credential is { IsConfirmed: false })
        {
            var user = await store.FindUserAsync(credential.UserId, cancellationToken);
            await SendConfirmationAsync(credential.UserId, normalized, user?.PreferredLanguage ?? language, links, cancellationToken);
        }
    }

    /// <summary>Sends a reset link to any account with this email, Google ones included; silent otherwise.</summary>
    public async Task ForgotPasswordAsync(string emailAddress, string language, AccountLinks links, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (!EmailAddresses.IsValid(emailAddress))
        {
            return;
        }

        var normalized = EmailAddresses.Normalize(emailAddress);
        var credential = await store.FindCredentialByEmailAsync(normalized, cancellationToken);
        var user = credential is not null
            ? await store.FindUserAsync(credential.UserId, cancellationToken)
            : await store.FindUserByEmailAsync(normalized, cancellationToken);
        if (user is null)
        {
            return;
        }

        var token = await IssueAsync(user.Id, EmailTokenPurpose.ResetPassword, cancellationToken);
        if (token is not null)
        {
            await email.SendAsync(AccountEmails.ResetPassword(normalized, user.PreferredLanguage, links.ResetPassword(token)), cancellationToken);
        }
    }

    /// <summary>Sets a new password from the emailed link; it also confirms the address (the link proves the mailbox).</summary>
    public async Task<LocalSignInOutcome> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var found = await store.FindUsableTokenAsync(Hash(token), EmailTokenPurpose.ResetPassword, now, cancellationToken);
        if (found is not { } usable)
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidToken);
        }

        var user = await store.FindUserAsync(usable.UserId, cancellationToken);
        if (user is null)
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidToken);
        }

        var credential = await store.FindCredentialByUserAsync(user.Id, cancellationToken);
        var loginEmail = credential?.Email ?? (user.Email is { } e && EmailAddresses.IsValid(e) ? EmailAddresses.Normalize(e) : null);
        if (loginEmail is null)
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidToken);
        }

        if (!IsStrongEnough(newPassword, loginEmail))
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.WeakPassword);
        }

        if (!await store.TryConsumeTokenAsync(usable.TokenId, now, cancellationToken))
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidToken);
        }

        var hash = hasher.Hash(newPassword);
        if (credential is null)
        {
            // A Google account adding a password.
            credential = PasswordCredential.Create(user, loginEmail, hash, now);
            credential.ConfirmEmail(now);
            if (!await store.TryAddCredentialAsync(credential, cancellationToken))
            {
                return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidToken);
            }
        }
        else
        {
            credential.ChangePassword(hash, now);
            credential.ConfirmEmail(now);
        }

        await store.RevokeTokensAsync(user.Id, EmailTokenPurpose.ResetPassword, now, cancellationToken);
        user.RecordLogin(now, null);
        await store.SaveChangesAsync(cancellationToken);
        return LocalSignInOutcome.Of(user);
    }

    /// <summary>Length is what makes a password hard to guess (NIST SP 800-63B): no composition rules.</summary>
    public static bool IsStrongEnough(string? password, string normalizedEmail)
    {
        if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            return false;
        }

        var lowered = password.ToLowerInvariant();
        var localPart = normalizedEmail.Split('@')[0];
        return lowered != normalizedEmail && lowered != localPart && password.Distinct().Count() > 2;
    }

    internal static string Hash(string? token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));

    private async Task<LocalSignInOutcome> CompleteSignInAsync(
        PasswordCredential credential,
        PasswordCheck check,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(credential.UserId, cancellationToken);
        if (user is null)
        {
            return LocalSignInOutcome.Fail(LocalSignInFailure.InvalidCredentials);
        }

        credential.RecordSuccess();
        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            credential.Rehash(hasher.Hash(password));
        }

        user.RecordLogin(now, null);
        await store.SaveChangesAsync(cancellationToken);
        return LocalSignInOutcome.Of(user);
    }

    private async Task SendConfirmationAsync(Guid userId, string address, string language, AccountLinks links, CancellationToken cancellationToken)
    {
        var token = await IssueAsync(userId, EmailTokenPurpose.ConfirmEmail, cancellationToken);
        if (token is not null)
        {
            await email.SendAsync(AccountEmails.ConfirmEmail(address, language, links.ConfirmEmail(token)), cancellationToken);
        }
    }

    private async Task SendAlreadyRegisteredAsync(Guid userId, string address, string language, AccountLinks links, CancellationToken cancellationToken)
    {
        var token = await IssueAsync(userId, EmailTokenPurpose.ResetPassword, cancellationToken);
        if (token is not null)
        {
            await email.SendAsync(AccountEmails.AlreadyRegistered(address, language, links.SignIn(), links.ResetPassword(token)), cancellationToken);
        }
    }

    /// <summary>A new random link, or null when this account already got <see cref="MaxEmailsPerHour"/> of this kind.</summary>
    private async Task<string?> IssueAsync(Guid userId, EmailTokenPurpose purpose, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (await store.CountTokensSinceAsync(userId, purpose, now.AddHours(-1), cancellationToken) >= MaxEmailsPerHour)
        {
            return null;
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        await store.AddTokenAsync(EmailToken.Issue(userId, purpose, Hash(token), now), cancellationToken);
        return token;
    }

    private string DummyHash() => dummyHash ??= hasher.Hash("not-a-real-password-" + Guid.NewGuid());

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DisplayNameOf(string? displayName, string normalizedEmail)
    {
        var name = displayName?.Trim();
        return string.IsNullOrEmpty(name) ? normalizedEmail.Split('@')[0] : name.Length > 200 ? name[..200] : name;
    }
}
