using PokerCoach.Domain.Notifications;

namespace PokerCoach.Application.Notifications;

public interface IWeeklyNotificationStore
{
    Task<bool> ExistsAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken);

    Task AddAsync(WeeklyNotification notification, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
