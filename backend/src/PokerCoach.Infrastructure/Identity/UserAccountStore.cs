using Microsoft.EntityFrameworkCore;
using Npgsql;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Identity;

internal sealed class UserAccountStore(PokerCoachDbContext db) : IUserAccountStore
{
    public Task<User?> FindByExternalIdentityAsync(string provider, string providerSubjectId, CancellationToken cancellationToken) =>
        (from identity in db.Set<ExternalIdentity>()
         join user in db.Set<User>() on identity.UserId equals user.Id
         where identity.Provider == provider && identity.ProviderSubjectId == providerSubjectId
         select user).FirstOrDefaultAsync(cancellationToken);

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task<bool> TryAddAsync(User user, ExternalIdentity identity, CancellationToken cancellationToken)
    {
        db.Add(user);
        db.Add(identity);
        try
        {
            // One SaveChanges = one transaction: the user is never created without its identity.
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Forget the rejected entities so the caller can load the winner's account.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> TryLinkAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        db.Add(identity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(identity).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
