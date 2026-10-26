using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Identity;

internal sealed class CoachBookmarkStore(PokerCoachDbContext db) : ICoachBookmarkStore
{
    public async Task AddAsync(CoachBookmark bookmark, CancellationToken cancellationToken)
    {
        db.Add(bookmark);
        await Task.CompletedTask;
    }

    public Task<CoachBookmark?> FindAsync(Guid bookmarkId, CancellationToken cancellationToken) =>
        db.Set<CoachBookmark>().FirstOrDefaultAsync(b => b.Id == bookmarkId, cancellationToken);

    public Task<CoachBookmark?> FindByCoachAndInvitationAsync(Guid coachUserId, Guid invitationId, CancellationToken cancellationToken) =>
        db.Set<CoachBookmark>().FirstOrDefaultAsync(b => b.CoachUserId == coachUserId && b.InvitationId == invitationId, cancellationToken);

    public async Task<IReadOnlyList<CoachBookmark>> ListByCoachAsync(Guid coachUserId, CancellationToken cancellationToken) =>
        await db.Set<CoachBookmark>()
            .Where(b => b.CoachUserId == coachUserId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task RemoveAsync(CoachBookmark bookmark, CancellationToken cancellationToken)
    {
        db.Remove(bookmark);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
