using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Tests.Identity;

internal sealed class InMemoryCoachBookmarkStore : ICoachBookmarkStore
{
    private readonly List<CoachBookmark> bookmarks = [];

    public IReadOnlyList<CoachBookmark> All => bookmarks;
    public int SaveCount { get; private set; }

    public Task AddAsync(CoachBookmark bookmark, CancellationToken cancellationToken)
    {
        bookmarks.Add(bookmark);
        return Task.CompletedTask;
    }

    public Task<CoachBookmark?> FindAsync(Guid bookmarkId, CancellationToken cancellationToken) =>
        Task.FromResult(bookmarks.Find(b => b.Id == bookmarkId));

    public Task<CoachBookmark?> FindByCoachAndInvitationAsync(Guid coachUserId, Guid invitationId, CancellationToken cancellationToken) =>
        Task.FromResult(bookmarks.Find(b => b.CoachUserId == coachUserId && b.InvitationId == invitationId));

    public Task<IReadOnlyList<CoachBookmark>> ListByCoachAsync(Guid coachUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CoachBookmark>>(
            bookmarks
                .Where(b => b.CoachUserId == coachUserId)
                .OrderByDescending(b => b.CreatedAt)
                .ToList());

    public Task RemoveAsync(CoachBookmark bookmark, CancellationToken cancellationToken)
    {
        bookmarks.Remove(bookmark);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
