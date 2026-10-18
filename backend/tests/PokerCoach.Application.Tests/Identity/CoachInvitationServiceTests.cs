using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Tests.Identity;

public sealed class CoachInvitationServiceTests
{
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid OtherStudentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(Now);
    private readonly InMemoryCoachInvitationStore store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CoachInvitationService Service() => new(store, time);

    // --- Create ---

    [Fact]
    public async Task Create_persists_invitation_with_correct_properties()
    {
        var inv = await Service().CreateAsync(StudentId, "Coach Dupont", null, Ct);

        Assert.Equal(StudentId, inv.StudentId);
        Assert.Equal("Coach Dupont", inv.Label);
        Assert.Equal(Now, inv.CreatedAt);
        Assert.Null(inv.ExpiresAt);
        Assert.True(inv.IsActive(Now));
        Assert.Equal(1, store.SaveCount);
        Assert.Single(store.All);
    }

    [Fact]
    public async Task Create_trims_label_whitespace()
    {
        var inv = await Service().CreateAsync(StudentId, "  Jean-Pierre  ", null, Ct);

        Assert.Equal("Jean-Pierre", inv.Label);
    }

    [Fact]
    public async Task Create_stores_expiry_when_provided()
    {
        var expiry = Now.AddDays(30);
        var inv = await Service().CreateAsync(StudentId, "Coach", expiry, Ct);

        Assert.Equal(expiry, inv.ExpiresAt);
        Assert.True(inv.IsActive(Now));
        Assert.False(inv.IsActive(expiry.AddSeconds(1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_throws_on_empty_label(string label)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().CreateAsync(StudentId, label, null, Ct));
    }

    [Fact]
    public async Task Create_throws_when_label_exceeds_max_length()
    {
        var longLabel = new string('x', CoachInvitation.MaxLabelLength + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Service().CreateAsync(StudentId, longLabel, null, Ct));
    }

    // --- Revoke ---

    [Fact]
    public async Task Revoke_deactivates_own_invitation_and_returns_true()
    {
        var inv = await Service().CreateAsync(StudentId, "Coach", null, Ct);

        var result = await Service().RevokeAsync(StudentId, inv.Id, Ct);

        Assert.True(result);
        Assert.False(inv.IsActive(Now));
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task Revoke_returns_false_for_another_students_invitation()
    {
        var inv = await Service().CreateAsync(StudentId, "Coach", null, Ct);

        var result = await Service().RevokeAsync(OtherStudentId, inv.Id, Ct);

        Assert.False(result);
        Assert.True(inv.IsActive(Now));
    }

    [Fact]
    public async Task Revoke_returns_false_for_unknown_id()
    {
        var result = await Service().RevokeAsync(StudentId, Guid.NewGuid(), Ct);

        Assert.False(result);
    }

    // --- List ---

    [Fact]
    public async Task List_returns_all_invitations_for_student_newest_first()
    {
        time.SetUtcNow(Now);
        var first = await Service().CreateAsync(StudentId, "Coach A", null, Ct);
        time.SetUtcNow(Now.AddHours(1));
        var second = await Service().CreateAsync(StudentId, "Coach B", null, Ct);

        var list = await Service().ListAsync(StudentId, Ct);

        Assert.Equal(2, list.Count);
        Assert.Equal(second.Id, list[0].Id);
        Assert.Equal(first.Id, list[1].Id);
    }

    [Fact]
    public async Task List_excludes_revoked_invitations()
    {
        var inv = await Service().CreateAsync(StudentId, "Coach", null, Ct);
        await Service().RevokeAsync(StudentId, inv.Id, Ct);

        var list = await Service().ListAsync(StudentId, Ct);

        Assert.Empty(list);
    }

    [Fact]
    public async Task List_does_not_return_other_students_invitations()
    {
        await Service().CreateAsync(StudentId, "Coach A", null, Ct);
        await Service().CreateAsync(OtherStudentId, "Coach B", null, Ct);

        var list = await Service().ListAsync(StudentId, Ct);

        Assert.Single(list);
        Assert.Equal(StudentId, list[0].StudentId);
    }

    // --- ResolveStudentId ---

    [Fact]
    public async Task ResolveStudentId_returns_student_id_for_active_token()
    {
        var inv = await Service().CreateAsync(StudentId, "Coach", null, Ct);

        var resolved = await Service().ResolveStudentIdAsync(inv.Id, Ct);

        Assert.Equal(StudentId, resolved);
    }

    [Fact]
    public async Task ResolveStudentId_returns_null_for_revoked_token()
    {
        var inv = await Service().CreateAsync(StudentId, "Coach", null, Ct);
        await Service().RevokeAsync(StudentId, inv.Id, Ct);

        var resolved = await Service().ResolveStudentIdAsync(inv.Id, Ct);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveStudentId_returns_null_for_expired_token()
    {
        var expiry = Now.AddDays(7);
        var inv = await Service().CreateAsync(StudentId, "Coach", expiry, Ct);

        time.SetUtcNow(expiry.AddSeconds(1));
        var resolved = await Service().ResolveStudentIdAsync(inv.Id, Ct);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveStudentId_returns_student_id_for_not_yet_expired_token()
    {
        var expiry = Now.AddDays(7);
        var inv = await Service().CreateAsync(StudentId, "Coach", expiry, Ct);

        time.SetUtcNow(expiry.AddSeconds(-1));
        var resolved = await Service().ResolveStudentIdAsync(inv.Id, Ct);

        Assert.Equal(StudentId, resolved);
    }

    [Fact]
    public async Task ResolveStudentId_returns_null_for_unknown_token()
    {
        var resolved = await Service().ResolveStudentIdAsync(Guid.NewGuid(), Ct);

        Assert.Null(resolved);
    }
}
