using PokerCoach.Application.Identity;
using PokerCoach.Application.Progress;
using PokerCoach.Domain.Notifications;
using PokerCoach.Domain.Progress;

namespace PokerCoach.Application.Notifications;

/// <summary>
/// Sends a weekly plan reminder to every user who has a plan for this week and has not yet received one.
/// Idempotent: safe to call more than once in the same week.
/// </summary>
public sealed class WeeklyNotificationService(
    IWeeklyPlanStore plans,
    IWeeklyNotificationStore notifications,
    IUserAccountStore users,
    IEmailSender email,
    TimeProvider time)
{
    /// <param name="baseUrl">The web app's public URL (e.g. "https://nutsiq.com/"), used to build the progress link.</param>
    /// <returns>Number of notifications sent.</returns>
    public async Task<int> SendAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        var weekStart = WeeklyPlanner.WeekStart(time.GetUtcNow());
        var link = new Uri(baseUrl, "progress").ToString();
        var userIds = await plans.ListUserIdsWithPlanAsync(weekStart, cancellationToken);
        var sent = 0;

        foreach (var userId in userIds)
        {
            if (await notifications.ExistsAsync(userId, weekStart, cancellationToken))
            {
                continue;
            }

            var user = await users.FindByIdAsync(userId, cancellationToken);
            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                continue;
            }

            var message = WeeklyNotificationEmail.WeeklyPlanReady(user.Email, user.PreferredLanguage, link);
            await email.SendAsync(message, cancellationToken);
            await notifications.AddAsync(WeeklyNotification.Create(userId, weekStart, time.GetUtcNow()), cancellationToken);
            await notifications.SaveChangesAsync(cancellationToken);
            sent++;
        }

        return sent;
    }
}
