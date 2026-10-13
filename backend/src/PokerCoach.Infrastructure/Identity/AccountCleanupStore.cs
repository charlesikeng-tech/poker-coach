using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Identity;

internal sealed class AccountCleanupStore(PokerCoachDbContext db) : IAccountCleanupStore
{
    public Task<int> DeleteExpiredUnconfirmedAsync(DateTimeOffset createdBefore, CancellationToken cancellationToken) =>
        db.Set<User>()
            .Where(u => u.CreatedAt < createdBefore
                && !db.Set<ExternalIdentity>().Any(i => i.UserId == u.Id)
                && db.Set<PasswordCredential>().Any(c => c.UserId == u.Id
                    && EF.Property<DateTimeOffset?>(c, "ConfirmedAt") == null))
            .ExecuteDeleteAsync(cancellationToken);
}
