using Microsoft.EntityFrameworkCore;
using Npgsql;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Identity;

internal sealed class LocalAccountStore(PokerCoachDbContext db) : ILocalAccountStore
{
    public Task<PasswordCredential?> FindCredentialByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Set<PasswordCredential>().FirstOrDefaultAsync(c => c.Email == normalizedEmail, cancellationToken);

    public Task<PasswordCredential?> FindCredentialByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<PasswordCredential>().FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    // Oldest first: if two accounts share an email (two Google accounts over time), the first one wins.
    public Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Set<User>()
            .Where(u => u.Email != null && u.Email.ToLower() == normalizedEmail)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task<bool> TryAddAsync(User user, PasswordCredential credential, CancellationToken cancellationToken)
    {
        db.Add(user);
        db.Add(credential);
        return await TrySaveAsync(cancellationToken, user, credential);
    }

    public async Task<bool> TryAddCredentialAsync(PasswordCredential credential, CancellationToken cancellationToken)
    {
        db.Add(credential);
        return await TrySaveAsync(cancellationToken, credential);
    }

    public async Task AddTokenAsync(EmailToken token, CancellationToken cancellationToken)
    {
        db.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountTokensSinceAsync(Guid userId, EmailTokenPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.Set<EmailToken>().CountAsync(t => t.UserId == userId && t.Purpose == purpose && t.CreatedAt >= since, cancellationToken);

    public async Task<(Guid TokenId, Guid UserId)?> FindUsableTokenAsync(string tokenHash, EmailTokenPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = await db.Set<EmailToken>().AsNoTracking()
            .Where(t => t.TokenHash == tokenHash && t.Purpose == purpose && t.UsedAt == null && t.ExpiresAt > now)
            .Select(t => new { t.Id, t.UserId })
            .FirstOrDefaultAsync(cancellationToken);
        return token is null ? null : (token.Id, token.UserId);
    }

    public async Task<bool> TryConsumeTokenAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Set<EmailToken>()
            .Where(t => t.Id == tokenId && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken) == 1;

    public Task RevokeTokensAsync(Guid userId, EmailTokenPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<EmailToken>()
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken, params object[] added)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            foreach (var entity in added)
            {
                db.Entry(entity).State = EntityState.Detached;
            }

            return false;
        }
    }
}

/// <summary>ASP.NET Core Identity's hasher (PBKDF2-HMAC-SHA512, 100,000 iterations in format v3), without the rest of Identity.</summary>
internal sealed class AspNetPasswordHasher : IPasswordHasher
{
    private static readonly object Subject = new();
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<object> hasher = new();

    public string Hash(string password) => hasher.HashPassword(Subject, password);

    public PasswordCheck Verify(string hash, string password) => hasher.VerifyHashedPassword(Subject, hash, password) switch
    {
        Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success => PasswordCheck.Success,
        Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
        _ => PasswordCheck.Failed,
    };
}
