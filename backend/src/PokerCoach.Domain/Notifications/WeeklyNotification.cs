namespace PokerCoach.Domain.Notifications;

/// <summary>Records that a weekly plan reminder was sent, so it is not sent twice the same week.</summary>
public sealed class WeeklyNotification
{
    // For EF Core materialization.
    private WeeklyNotification()
    {
    }

    private WeeklyNotification(Guid userId, DateOnly weekStart, DateTimeOffset sentAt)
    {
        UserId = userId;
        WeekStart = weekStart;
        SentAt = sentAt;
    }

    public Guid UserId { get; private set; }

    public DateOnly WeekStart { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public static WeeklyNotification Create(Guid userId, DateOnly weekStart, DateTimeOffset now) =>
        new(userId, weekStart, now);
}
