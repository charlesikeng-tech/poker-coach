using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Identity;

public interface ICoachInvitationStore
{
    Task AddAsync(CoachInvitation invitation, CancellationToken cancellationToken);

    Task<CoachInvitation?> FindAsync(Guid invitationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CoachInvitation>> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
