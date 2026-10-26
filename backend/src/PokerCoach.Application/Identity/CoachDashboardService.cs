using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Subscriptions;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;

namespace PokerCoach.Application.Identity;

public sealed record TrendPoint(DateOnly Month, decimal? Vpip, decimal? Pfr, decimal? ThreeBet);

public sealed record StudentSummary(
    Guid BookmarkId,
    Guid InvitationId,
    string StudentName,
    string Plan,
    int LeakCount,
    int Hands,
    decimal? Vpip,
    decimal? Pfr,
    decimal? ThreeBet,
    string? Notes,
    IReadOnlyList<TrendPoint> Trend);

public sealed class CoachDashboardService(
    ICoachBookmarkStore bookmarks,
    ICoachInvitationStore invitations,
    IUserAccountStore users,
    ILeakService leaks,
    IStatisticsReadStore statisticsStore,
    PlanAccess plans,
    TimeProvider time)
{
    private const int TrendMonths = 6;

    public async Task<CoachBookmark> AddStudentAsync(Guid coachUserId, Guid invitationToken, CancellationToken cancellationToken)
    {
        var invitation = await invitations.FindAsync(invitationToken, cancellationToken);
        if (invitation is null || !invitation.IsActive(time.GetUtcNow()))
        {
            throw new InvalidOperationException("Invitation is invalid or expired.");
        }

        if (invitation.StudentId == coachUserId)
        {
            throw new InvalidOperationException("Cannot bookmark your own invitation.");
        }

        var existing = await bookmarks.FindByCoachAndInvitationAsync(coachUserId, invitationToken, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var bookmark = CoachBookmark.Create(coachUserId, invitationToken, time.GetUtcNow());
        await bookmarks.AddAsync(bookmark, cancellationToken);
        await bookmarks.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    public async Task<bool> RemoveStudentAsync(Guid coachUserId, Guid bookmarkId, CancellationToken cancellationToken)
    {
        var bookmark = await bookmarks.FindAsync(bookmarkId, cancellationToken);
        if (bookmark is null || bookmark.CoachUserId != coachUserId)
        {
            return false;
        }

        await bookmarks.RemoveAsync(bookmark, cancellationToken);
        await bookmarks.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateStudentNotesAsync(Guid coachUserId, Guid bookmarkId, string? notes, CancellationToken cancellationToken)
    {
        var bookmark = await bookmarks.FindAsync(bookmarkId, cancellationToken);
        if (bookmark is null || bookmark.CoachUserId != coachUserId)
        {
            return false;
        }

        bookmark.UpdateNotes(notes);
        await bookmarks.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<StudentSummary>> ListStudentsAsync(Guid coachUserId, CancellationToken cancellationToken)
    {
        var list = await bookmarks.ListByCoachAsync(coachUserId, cancellationToken);
        var summaries = new List<StudentSummary>(list.Count);

        foreach (var bookmark in list)
        {
            var invitation = await invitations.FindAsync(bookmark.InvitationId, cancellationToken);
            if (invitation is null || !invitation.IsActive(time.GetUtcNow()))
            {
                continue;
            }

            var studentId = invitation.StudentId;
            var user = await users.FindByIdAsync(studentId, cancellationToken);
            if (user is null)
            {
                continue;
            }

            var plan = await plans.GetPlanAsync(studentId, cancellationToken);
            var filter = new StatisticsFilter(null, null, ReferenceRanges.MinStackBigBlinds, null);
            var groups = await statisticsStore.CountByPositionAsync(studentId, filter, HeroHandFacts.Version, cancellationToken);
            var overall = groups.Aggregate(HeroStatCounts.Zero, (acc, g) => acc.Add(g.Counts));
            var line = StatLine.From(overall);

            var leakAnalysis = await leaks.GetAsync(studentId, null, null, null, cancellationToken);

            var now = time.GetUtcNow();
            var firstMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-(TrendMonths - 1));
            var months = await statisticsStore.CountByMonthAsync(studentId, filter with { From = firstMonth }, HeroHandFacts.Version, cancellationToken);
            var trend = months
                .Select(m =>
                {
                    var sl = StatLine.From(m.Counts);
                    return new TrendPoint(m.Month, sl.Vpip.Rate, sl.Pfr.Rate, sl.ThreeBet.Rate);
                })
                .ToList();

            summaries.Add(new StudentSummary(
                bookmark.Id,
                bookmark.InvitationId,
                user.DisplayName,
                plan.ToString(),
                leakAnalysis.Report.Leaks.Count,
                line.Hands,
                line.Vpip.Rate,
                line.Pfr.Rate,
                line.ThreeBet.Rate,
                bookmark.Notes,
                trend));
        }

        return summaries;
    }
}
