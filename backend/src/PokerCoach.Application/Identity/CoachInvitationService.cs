using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Identity;

public sealed class CoachInvitationService(ICoachInvitationStore store, TimeProvider time)
{
    public async Task<CoachInvitation> CreateAsync(
        Guid studentId,
        string label,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        var invitation = CoachInvitation.Create(studentId, label, expiresAt, time.GetUtcNow());
        await store.AddAsync(invitation, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    /// <summary>Returns false when the invitation does not exist or belongs to another student.</summary>
    public async Task<bool> RevokeAsync(Guid studentId, Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await store.FindAsync(invitationId, cancellationToken);
        if (invitation is null || invitation.StudentId != studentId)
        {
            return false;
        }

        invitation.Revoke(time.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<CoachInvitation>> ListAsync(Guid studentId, CancellationToken cancellationToken) =>
        store.ListByStudentAsync(studentId, cancellationToken);

    /// <summary>Returns the student's id when the token is valid and active; null otherwise.</summary>
    public async Task<Guid?> ResolveStudentIdAsync(Guid token, CancellationToken cancellationToken)
    {
        var invitation = await store.FindAsync(token, cancellationToken);
        return invitation?.IsActive(time.GetUtcNow()) is true ? invitation.StudentId : null;
    }
}
