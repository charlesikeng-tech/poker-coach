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
}
