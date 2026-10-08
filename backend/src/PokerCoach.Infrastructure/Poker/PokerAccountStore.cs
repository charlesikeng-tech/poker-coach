using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Poker;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Poker;

internal sealed class PokerAccountStore(PokerCoachDbContext db) : IPokerAccountStore
{
    public async Task<IReadOnlyList<PokerAccountView>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<PokerAccountRecord>()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new PokerAccountView(a.Id, a.Room, a.ScreenName, a.CreatedAt, a.ConfirmedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> ConfirmAsync(Guid userId, Guid accountId, DateTimeOffset confirmedAt, CancellationToken cancellationToken) =>
        await db.Set<PokerAccountRecord>()
            .Where(a => a.Id == accountId && a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ConfirmedAt, a => a.ConfirmedAt ?? confirmedAt), cancellationToken) == 1;

    /// <summary>Tournaments, hands and imported files go with it (cascading foreign keys).</summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid accountId, CancellationToken cancellationToken) =>
        await db.Set<PokerAccountRecord>()
            .Where(a => a.Id == accountId && a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken) == 1;
}
