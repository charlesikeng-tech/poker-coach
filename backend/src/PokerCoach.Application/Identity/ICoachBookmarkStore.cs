using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Identity;

public interface ICoachBookmarkStore
{
    Task AddAsync(CoachBookmark bookmark, CancellationToken cancellationToken);

    Task<CoachBookmark?> FindAsync(Guid bookmarkId, CancellationToken cancellationToken);

    Task<CoachBookmark?> FindByCoachAndInvitationAsync(Guid coachUserId, Guid invitationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CoachBookmark>> ListByCoachAsync(Guid coachUserId, CancellationToken cancellationToken);

    Task RemoveAsync(CoachBookmark bookmark, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
