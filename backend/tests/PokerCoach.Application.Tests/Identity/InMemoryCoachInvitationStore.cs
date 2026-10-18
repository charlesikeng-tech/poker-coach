using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Tests.Identity;

internal sealed class InMemoryCoachInvitationStore : ICoachInvitationStore
{
    private readonly List<CoachInvitation> invitations = [];

    public IReadOnlyList<CoachInvitation> All => invitations;
    public int SaveCount { get; private set; }

    public Task AddAsync(CoachInvitation invitation, CancellationToken cancellationToken)
    {
        invitations.Add(invitation);
        return Task.CompletedTask;
    }

    public Task<CoachInvitation?> FindAsync(Guid invitationId, CancellationToken cancellationToken) =>
        Task.FromResult(invitations.Find(i => i.Id == invitationId));

    public Task<IReadOnlyList<CoachInvitation>> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CoachInvitation>>(
            invitations
                .Where(i => i.StudentId == studentId && i.RevokedAt == null)
                .OrderByDescending(i => i.CreatedAt)
                .ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
