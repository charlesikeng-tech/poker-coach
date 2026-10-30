using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Subscriptions;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Billing;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations", "subscriptions");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Email).HasMaxLength(Invitation.EmailMaxLength).IsRequired();
        builder.Property(i => i.Plan).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.Reason).HasMaxLength(Invitation.ReasonMaxLength).IsRequired();
        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.ExpiresAt).IsRequired();

        // One active invitation per email; the save logic replaces a previous one.
        builder.HasIndex(i => i.Email);
    }
}

internal sealed class InvitationStore(PokerCoachDbContext db) : IInvitationStore
{
    public Task<Invitation?> FindActiveByEmailAsync(string normalizedEmail, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<Invitation>()
            .FirstOrDefaultAsync(i => i.Email == normalizedEmail && i.AcceptedAt == null && i.ExpiresAt > now, cancellationToken);

    public Task<Invitation?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Invitation>().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task SaveAsync(Invitation invitation, CancellationToken cancellationToken)
    {
        // Replace any existing active invitation for the same email.
        await db.Set<Invitation>()
            .Where(i => i.Email == invitation.Email && i.AcceptedAt == null)
            .ExecuteDeleteAsync(cancellationToken);

        db.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<Invitation>().Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<InvitationView>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<Invitation>()
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationView(i.Id, i.Email, i.Plan, i.Reason, i.GrantEndsAt, i.ExpiresAt, i.AcceptedAt, i.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
