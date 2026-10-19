using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerCoach.Application.Notifications;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Notifications;
using PokerCoach.Infrastructure.Persistence;

namespace PokerCoach.Infrastructure.Notifications;

internal static class NotificationsSchema
{
    public const string Name = "notifications";
}

internal sealed class WeeklyNotificationConfiguration : IEntityTypeConfiguration<WeeklyNotification>
{
    public void Configure(EntityTypeBuilder<WeeklyNotification> builder)
    {
        builder.ToTable("weekly_notifications", NotificationsSchema.Name);
        builder.HasKey(n => new { n.UserId, n.WeekStart });
        builder.Property(n => n.SentAt).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WeeklyNotificationStore(PokerCoachDbContext db) : IWeeklyNotificationStore
{
    public Task<bool> ExistsAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken) =>
        db.Set<WeeklyNotification>().AnyAsync(n => n.UserId == userId && n.WeekStart == weekStart, cancellationToken);

    public Task AddAsync(WeeklyNotification notification, CancellationToken cancellationToken)
    {
        db.Add(notification);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
