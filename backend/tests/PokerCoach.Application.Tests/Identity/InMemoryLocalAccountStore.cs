using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Tests.Identity;

/// <summary>Same contract as the database store: unique email, single-use tokens. Shares users with <see cref="InMemoryUserAccountStore"/>.</summary>
internal sealed class InMemoryLocalAccountStore(InMemoryUserAccountStore users) : ILocalAccountStore
{
    private readonly List<PasswordCredential> credentials = [];
    private readonly List<(EmailToken Token, DateTimeOffset? UsedAt)> tokens = [];

    public IReadOnlyList<PasswordCredential> Credentials => credentials;

    public Task<PasswordCredential?> FindCredentialByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(credentials.Find(c => c.Email == normalizedEmail));

    public Task<PasswordCredential?> FindCredentialByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(credentials.Find(c => c.UserId == userId));

    public Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(users.Users.Where(u => u.Email?.ToLowerInvariant() == normalizedEmail).OrderBy(u => u.CreatedAt).FirstOrDefault());

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        users.FindByIdAsync(userId, cancellationToken);

    public Task<bool> TryAddAsync(User user, PasswordCredential credential, CancellationToken cancellationToken)
    {
        if (credentials.Exists(c => c.Email == credential.Email))
        {
            return Task.FromResult(false);
        }

        users.AddUser(user);
        credentials.Add(credential);
        return Task.FromResult(true);
    }

    public Task<bool> TryAddCredentialAsync(PasswordCredential credential, CancellationToken cancellationToken)
    {
        if (credentials.Exists(c => c.Email == credential.Email || c.UserId == credential.UserId))
        {
            return Task.FromResult(false);
        }

        credentials.Add(credential);
        return Task.FromResult(true);
    }

    public Task AddTokenAsync(EmailToken token, CancellationToken cancellationToken)
    {
        tokens.Add((token, null));
        return Task.CompletedTask;
    }

    public Task<int> CountTokensSinceAsync(Guid userId, EmailTokenPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult(tokens.Count(t => t.Token.UserId == userId && t.Token.Purpose == purpose && t.Token.CreatedAt >= since));

    public Task<(Guid TokenId, Guid UserId)?> FindUsableTokenAsync(string tokenHash, EmailTokenPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var found = tokens.FirstOrDefault(t => t.Token.TokenHash == tokenHash && t.Token.Purpose == purpose && t.UsedAt is null && t.Token.ExpiresAt > now);
        return Task.FromResult<(Guid, Guid)?>(found.Token is null ? null : (found.Token.Id, found.Token.UserId));
    }

    public Task<bool> TryConsumeTokenAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var index = tokens.FindIndex(t => t.Token.Id == tokenId && t.UsedAt is null && t.Token.ExpiresAt > now);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        tokens[index] = (tokens[index].Token, now);
        return Task.FromResult(true);
    }

    public Task RevokeTokensAsync(Guid userId, EmailTokenPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Token.UserId == userId && tokens[i].Token.Purpose == purpose && tokens[i].UsedAt is null)
            {
                tokens[i] = (tokens[i].Token, now);
            }
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
