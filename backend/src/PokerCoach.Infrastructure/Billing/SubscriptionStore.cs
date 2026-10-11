using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Subscriptions;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Billing;

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions", "subscriptions");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.CustomerId).HasMaxLength(255).IsRequired();
        builder.Property(s => s.SubscriptionId).HasMaxLength(255);

        // Stripe's names, readable in SQL and stable if Stripe adds a status.
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.Interval).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(s => s.Plan);

        // Webhooks find the user by customer: one customer per user, one user per customer.
        builder.HasIndex(s => s.CustomerId).IsUnique();
        builder.HasOne<User>().WithOne().HasForeignKey<Subscription>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProGrantConfiguration : IEntityTypeConfiguration<ProGrant>
{
    public void Configure(EntityTypeBuilder<ProGrant> builder)
    {
        builder.ToTable("pro_grants", "subscriptions");
        builder.HasKey(g => g.UserId);
        builder.Property(g => g.Reason).HasMaxLength(ProGrant.ReasonMaxLength).IsRequired();

        // The author is kept for the record only: deleting an administrator's account keeps their grants.
        builder.HasOne<User>().WithOne().HasForeignKey<ProGrant>(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SubscriptionStore(PokerCoachDbContext db) : ISubscriptionStore
{
    public Task<Subscription?> FindByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<Subscription>().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public Task<Subscription?> FindByCustomerAsync(string customerId, CancellationToken cancellationToken) =>
        db.Set<Subscription>().FirstOrDefaultAsync(s => s.CustomerId == customerId, cancellationToken);

    public async Task<bool> TryAddAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        db.Add(subscription);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(subscription).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public Task<ProGrant?> FindGrantAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<ProGrant>().FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);

    public async Task SaveGrantAsync(ProGrant grant, CancellationToken cancellationToken)
    {
        // Administrators act one at a time: a lost race between two grants for one user ends with one of them.
        var current = await FindGrantAsync(grant.UserId, cancellationToken);
        if (current is null)
        {
            db.Add(grant);
        }
        else
        {
            current.Renew(grant);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteGrantAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<ProGrant>().Where(g => g.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<GrantView>> ListGrantsAsync(CancellationToken cancellationToken) =>
        await (from g in db.Set<ProGrant>().AsNoTracking()
               join u in db.Set<User>() on g.UserId equals u.Id
               orderby g.EndsAt == null, g.EndsAt, g.GrantedAt descending
               select new GrantView(u.Id, u.DisplayName, u.Email, g.Reason, g.EndsAt, g.GrantedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriberView>> ListSubscribersAsync(int limit, CancellationToken cancellationToken) =>
        await (from s in db.Set<Subscription>().AsNoTracking()
               join u in db.Set<User>() on s.UserId equals u.Id
               where s.Status != null
               orderby s.SyncedAt descending
               select new SubscriberView(u.Id, u.DisplayName, u.Email, s.CustomerId, s.Status, s.Interval, s.CurrentPeriodEnd, s.CancelAtPeriodEnd))
            .Take(limit)
            .ToListAsync(cancellationToken);
}
