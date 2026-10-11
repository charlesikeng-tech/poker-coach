using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.IntegrationTests;

/// <summary>The AI usage ledger on PostgreSQL (ADR-0017): reservations hold under concurrency, settlements count right.</summary>
public sealed class AiUsageLedgerTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Concurrent_reservations_cannot_overrun_a_users_credits()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = await AddUserAsync(now);

        // Twenty requests at once for a user with three credits: exactly three get through.
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var ledger = scope.ServiceProvider.GetRequiredService<IAiUsageLedger>();
            return await ledger.ReserveAsync(
                Request(userId, AiTask.LeakExplanation, now),
                usage => usage.CreditsUsedThisMonth + 1 > 3 ? CoachingFailure.PlanLimitReached : null,
                Ct);
        }));

        Assert.Equal(3, results.Count(r => r is null));
        Assert.Equal(17, results.Count(r => r == CoachingFailure.PlanLimitReached));
        await using var check = fixture.Services.CreateAsyncScope();
        Assert.Equal(3, await check.ServiceProvider.GetRequiredService<IAiUsageLedger>().CreditsUsedAsync(userId, Month(now), now.AddMinutes(-10), Ct));
    }

    [Fact]
    public async Task Settlements_count_the_real_cost_once_and_failures_free_their_credits()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = await AddUserAsync(now);
        var billed = Request(userId, AiTask.TournamentDebrief, now);
        var failed = Request(userId, AiTask.WeekReview, now);
        var lost = Request(userId, AiTask.LeakExplanation, now);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var ledger = scope.ServiceProvider.GetRequiredService<IAiUsageLedger>();
            AiUsageSnapshot? before = null;
            foreach (var request in new[] { billed, failed, lost })
            {
                Assert.Null(await ledger.ReserveAsync(request, usage => { before ??= usage; return null; }, Ct));
            }

            var usage = new ModelUsage("claude-test", 9_000, 3_000, 0, 0);
            await ledger.SettleAsync(billed.Id, new AiSettlement(true, 5, usage, 0.048m, now), Ct);
            await ledger.SettleAsync(billed.Id, new AiSettlement(false, 0, null, null, now), Ct);
            await ledger.SettleAsync(failed.Id, new AiSettlement(false, 0, usage, 0.048m, now), Ct);
            await ledger.SettleAsync(lost.Id, new AiSettlement(false, 0, null, null, now), Ct);

            AiUsageSnapshot? after = null;
            Assert.Equal(
                CoachingFailure.DailyLimitReached,
                await ledger.ReserveAsync(Request(userId, AiTask.WeekReview, now), u => { after = u; return CoachingFailure.DailyLimitReached; }, Ct));

            // Debrief billed (5 credits); the failed review counts for the daily limit, not for the month's reviews.
            Assert.Equal((5, 1, 0), (after!.CreditsUsedThisMonth, after.CallsToday, after.CallsThisMonth));

            // 0.048 + 0.048 real, plus the explanation's ceiling (billing unknown), on top of what was already spent.
            Assert.Equal(before!.SpendThisMonthUsd + 0.096m + lost.CeilingUsd, after.SpendThisMonthUsd);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<IAiUsageLedger>().ReportAsync(Month(now), 500, Ct);
            var mine = Assert.Single(report.TopUsers, u => u.UserId == userId);
            Assert.Equal((5, 0.096m + lost.CeilingUsd), (mine.Credits, mine.CostUsd));
            Assert.NotNull(mine.Email);
            Assert.True(report.Tasks.Single(t => t.Task == AiTask.LeakExplanation).UnknownCost >= 1);
        }
    }

    private static DateTimeOffset Month(DateTimeOffset now) => AiUsageGuard.MonthStart(now);

    private static AiReservationRequest Request(Guid userId, AiTask task, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), userId, task, task.Credits(), 0.25m, now, Month(now), new DateTimeOffset(now.Date, TimeSpan.Zero), now.AddMinutes(-10), decimal.MaxValue);

    private async Task<Guid> AddUserAsync(DateTimeOffset now)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var user = User.Register("Jo", $"{Guid.NewGuid():N}@example.com", "fr", now);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IUserAccountStore>()
            .TryAddAsync(user, ExternalIdentity.Link(user, IdentityProviders.Google, Guid.NewGuid().ToString("N"), user.Email, now), Ct));
        return user.Id;
    }
}
