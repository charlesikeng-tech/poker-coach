using Microsoft.EntityFrameworkCore;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Identity;

internal sealed class CoachInvitationStore(PokerCoachDbContext db) : ICoachInvitationStore
{
    public async Task AddAsync(CoachInvitation invitation, CancellationToken cancellationToken)
    {
        db.Add(invitation);
        await Task.CompletedTask;
    }

    public Task<CoachInvitation?> FindAsync(Guid invitationId, CancellationToken cancellationToken) =>
        db.Set<CoachInvitation>().FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<CoachInvitation>> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        await db.Set<CoachInvitation>()
            .Where(i => i.StudentId == studentId && i.RevokedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
