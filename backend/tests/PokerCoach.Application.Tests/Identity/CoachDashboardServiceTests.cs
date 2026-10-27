using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Leaks;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Tests.Subscriptions;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Identity;

public sealed class CoachDashboardServiceTests
{
    private static readonly Guid CoachId = Guid.NewGuid();
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(Now);
    private readonly InMemoryCoachBookmarkStore bookmarkStore = new();
    private readonly InMemoryCoachInvitationStore invitationStore = new();
    private readonly InMemoryUserAccountStore userStore = new();
    private readonly StubStatisticsReadStore statsStore = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CoachDashboardService Service() => new(
        bookmarkStore,
        invitationStore,
        userStore,
        new StubLeakService(),
        statsStore,
        BillingFakes.Unbilled(time),
        time);

    private async Task<CoachInvitation> CreateInvitationAsync()
    {
        var inv = CoachInvitation.Create(StudentId, "Coach", null, Now);
        await invitationStore.AddAsync(inv, Ct);
        await invitationStore.SaveChangesAsync(Ct);
        return inv;
    }

    // --- AddStudentAsync ---

    [Fact]
    public async Task Add_creates_bookmark_for_valid_invitation()
    {
        var inv = await CreateInvitationAsync();

        var bookmark = await Service().AddStudentAsync(CoachId, inv.Id, Ct);

        Assert.Equal(CoachId, bookmark.CoachUserId);
        Assert.Equal(inv.Id, bookmark.InvitationId);
        Assert.Single(bookmarkStore.All);
    }

    [Fact]
    public async Task Add_returns_existing_bookmark_on_duplicate()
    {
        var inv = await CreateInvitationAsync();

        var first = await Service().AddStudentAsync(CoachId, inv.Id, Ct);
        var second = await Service().AddStudentAsync(CoachId, inv.Id, Ct);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(bookmarkStore.All);
    }

    [Fact]
    public async Task Add_throws_for_unknown_token()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().AddStudentAsync(CoachId, Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task Add_throws_for_expired_invitation()
    {
        var inv = CoachInvitation.Create(StudentId, "Coach", Now.AddHours(-1), Now.AddHours(-2));
        await invitationStore.AddAsync(inv, Ct);
        await invitationStore.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().AddStudentAsync(CoachId, inv.Id, Ct));
    }

    [Fact]
    public async Task Add_throws_for_revoked_invitation()
    {
        var inv = await CreateInvitationAsync();
        inv.Revoke(Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().AddStudentAsync(CoachId, inv.Id, Ct));
    }

    [Fact]
    public async Task Add_throws_when_bookmarking_own_invitation()
    {
        var inv = CoachInvitation.Create(CoachId, "Self", null, Now);
        await invitationStore.AddAsync(inv, Ct);
        await invitationStore.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().AddStudentAsync(CoachId, inv.Id, Ct));
    }

    // --- RemoveStudentAsync ---

    [Fact]
    public async Task Remove_deletes_own_bookmark()
    {
        var inv = await CreateInvitationAsync();
        var bookmark = await Service().AddStudentAsync(CoachId, inv.Id, Ct);

        var result = await Service().RemoveStudentAsync(CoachId, bookmark.Id, Ct);

        Assert.True(result);
        Assert.Empty(bookmarkStore.All);
    }

    [Fact]
    public async Task Remove_returns_false_for_unknown_bookmark()
    {
        var result = await Service().RemoveStudentAsync(CoachId, Guid.NewGuid(), Ct);

        Assert.False(result);
    }

    [Fact]
    public async Task Remove_returns_false_for_another_coachs_bookmark()
    {
        var inv = await CreateInvitationAsync();
        var bookmark = await Service().AddStudentAsync(CoachId, inv.Id, Ct);

        var result = await Service().RemoveStudentAsync(Guid.NewGuid(), bookmark.Id, Ct);

        Assert.False(result);
        Assert.Single(bookmarkStore.All);
    }

    // --- UpdateStudentNotesAsync ---

    [Fact]
    public async Task UpdateNotes_saves_notes_on_own_bookmark()
    {
        var inv = await CreateInvitationAsync();
        var bookmark = await Service().AddStudentAsync(CoachId, inv.Id, Ct);

        var result = await Service().UpdateStudentNotesAsync(CoachId, bookmark.Id, "Travailler les 3-bet OOP", Ct);

        Assert.True(result);
        Assert.Equal("Travailler les 3-bet OOP", bookmarkStore.All[0].Notes);
    }

    [Fact]
    public async Task UpdateNotes_clears_notes_with_null()
    {
        var inv = await CreateInvitationAsync();
        var bookmark = await Service().AddStudentAsync(CoachId, inv.Id, Ct);
        await Service().UpdateStudentNotesAsync(CoachId, bookmark.Id, "some notes", Ct);

        var result = await Service().UpdateStudentNotesAsync(CoachId, bookmark.Id, null, Ct);

        Assert.True(result);
        Assert.Null(bookmarkStore.All[0].Notes);
    }

    [Fact]
    public async Task UpdateNotes_returns_false_for_unknown_bookmark()
    {
        var result = await Service().UpdateStudentNotesAsync(CoachId, Guid.NewGuid(), "notes", Ct);

        Assert.False(result);
    }

    [Fact]
    public async Task UpdateNotes_returns_false_for_another_coachs_bookmark()
    {
        var inv = await CreateInvitationAsync();
        var bookmark = await Service().AddStudentAsync(CoachId, inv.Id, Ct);

        var result = await Service().UpdateStudentNotesAsync(Guid.NewGuid(), bookmark.Id, "notes", Ct);

        Assert.False(result);
        Assert.Null(bookmarkStore.All[0].Notes);
    }

    // --- ListStudentsAsync ---

    [Fact]
    public async Task List_returns_student_summary_for_active_bookmark()
    {
        var student = User.Register("student", "student@test.com", "fr", Now);
        userStore.AddUser(student);
        var inv = CoachInvitation.Create(student.Id, "Coach", null, Now);
        await invitationStore.AddAsync(inv, Ct);
        await invitationStore.SaveChangesAsync(Ct);

        await Service().AddStudentAsync(CoachId, inv.Id, Ct);
        var list = await Service().ListStudentsAsync(CoachId, Ct);

        Assert.Single(list);
        Assert.Equal("student", list[0].StudentName);
    }

    [Fact]
    public async Task List_skips_revoked_invitation()
    {
        var student = User.Register("student", "student@test.com", "fr", Now);
        userStore.AddUser(student);
        var inv = CoachInvitation.Create(student.Id, "Coach", null, Now);
        await invitationStore.AddAsync(inv, Ct);
        await invitationStore.SaveChangesAsync(Ct);

        await Service().AddStudentAsync(CoachId, inv.Id, Ct);
        inv.Revoke(Now);

        var list = await Service().ListStudentsAsync(CoachId, Ct);

        Assert.Empty(list);
    }

    [Fact]
    public async Task List_includes_notes_from_bookmark()
    {
        var student = User.Register("student", "student@test.com", "fr", Now);
        userStore.AddUser(student);
        var inv = CoachInvitation.Create(student.Id, "Coach", null, Now);
        await invitationStore.AddAsync(inv, Ct);
        await invitationStore.SaveChangesAsync(Ct);

        var bookmark = await Service().AddStudentAsync(CoachId, inv.Id, Ct);
        await Service().UpdateStudentNotesAsync(CoachId, bookmark.Id, "Focus 3-bet", Ct);

        var list = await Service().ListStudentsAsync(CoachId, Ct);

        Assert.Single(list);
        Assert.Equal("Focus 3-bet", list[0].Notes);
    }

    [Fact]
    public async Task List_includes_trend_points()
    {
        var student = User.Register("student", "student@test.com", "fr", Now);
        userStore.AddUser(student);
        var inv = CoachInvitation.Create(student.Id, "Coach", null, Now);
        await invitationStore.AddAsync(inv, Ct);
        await invitationStore.SaveChangesAsync(Ct);

        statsStore.MonthlyData[student.Id] =
        [
            (new DateOnly(2026, 9, 1), new HeroStatCounts(100, 100, 25, 20, 0, 0, 0, 0, 0, 50, 5, 0, 0, 0, 0, 0, 0, 0, 0m)),
            (new DateOnly(2026, 10, 1), new HeroStatCounts(200, 200, 50, 40, 0, 0, 0, 0, 0, 100, 12, 0, 0, 0, 0, 0, 0, 0, 0m)),
        ];

        await Service().AddStudentAsync(CoachId, inv.Id, Ct);
        var list = await Service().ListStudentsAsync(CoachId, Ct);

        Assert.Single(list);
        Assert.Equal(2, list[0].Trend.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), list[0].Trend[0].Month);
        Assert.Equal(0.25m, list[0].Trend[0].Vpip);
        Assert.Equal(new DateOnly(2026, 10, 1), list[0].Trend[1].Month);
    }

    // --- Stubs for dependencies not under test ---

    private sealed class StubLeakService : ILeakService
    {
        public Task<LeakAnalysis> GetAsync(Guid userId, TableFormat? format, DateTimeOffset? from, DateTimeOffset? to, StakesLevel stakesLevel, CancellationToken cancellationToken, GameType? gameType = null, TournamentSubFormat? subFormat = null) =>
            Task.FromResult(new LeakAnalysis(new LeakReport([], []), 0, new StatisticsSample(0, 0), 0, 1, TableFormat.SixMax, new FormatCounts(0, 0)));

        public Task<LeakCounts> CountAsync(Guid userId, TableFormat format, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken, GameType? gameType = null, TournamentSubFormat? subFormat = null) =>
            Task.FromResult(new LeakCounts(format, HeroStatCounts.Zero, new Dictionary<PokerPosition, HeroStatCounts>()));
    }

    internal sealed class StubStatisticsReadStore : IStatisticsReadStore
    {
        public Dictionary<Guid, List<(DateOnly Month, HeroStatCounts Counts)>> MonthlyData { get; } = [];

        public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(PokerPosition?, HeroStatCounts)>>([]);

        public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new StatisticsSample(0, 0));

        public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new FormatCounts(0, 0));

        public Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(DateOnly, HeroStatCounts)>>(
                MonthlyData.TryGetValue(userId, out var data) ? data : []);

        public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
