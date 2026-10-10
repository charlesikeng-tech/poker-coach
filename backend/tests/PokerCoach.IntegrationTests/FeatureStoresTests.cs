using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Application.Bankroll;
using PokerCoach.Application.Coaching;
using PokerCoach.Application.Tournaments;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Import;
using PokerCoach.Application.Poker;
using PokerCoach.Application.Progress;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Training;
using PokerCoach.Domain.Bankroll;
using PokerCoach.Domain.Identity;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Leaks;
using PokerCoach.Domain.Poker.Ranges;
using PokerCoach.Domain.Progress;
using PokerCoach.Domain.Training;

namespace PokerCoach.IntegrationTests;

/// <summary>
/// The SQL behind the bankroll, the weekly plan, the breakdowns, the quiz on real hands and account
/// deletion: raw upserts, jsonb casts, grouping by month and FK cascades only fail against PostgreSQL.
/// </summary>
public sealed class FeatureStoresTests(PostgresFixture fixture)
{
    private const string AcceleratorHands = "20260916_ACCELERATOR_1169257027__real_holdem_no-limit.txt";
    private const string AcceleratorSummary = "20260916_ACCELERATOR_1169257027__real_holdem_no-limit_summary.txt";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bankroll_settings_are_replaced_and_movements_deleted_by_their_owner_only()
    {
        var userId = await CreateUserAsync();
        var other = await CreateUserAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IBankrollStore>();
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        await store.SaveSettingsAsync(userId, new BankrollSettings(500m, start, BuyInRule.Standard), Ct);
        await store.SaveSettingsAsync(userId, new BankrollSettings(800m, start, BuyInRule.Conservative), Ct);
        var movement = new BankrollMovement(Guid.NewGuid(), BankrollMovementKind.Deposit, 50m, start.AddDays(3), "Rakeback");
        await store.AddMovementAsync(userId, movement, Ct);

        Assert.Equal(new BankrollSettings(800m, start, BuyInRule.Conservative), await store.GetSettingsAsync(userId, Ct));
        Assert.Equal(movement, Assert.Single(await store.ListMovementsAsync(userId, Ct)));
        Assert.False(await store.DeleteMovementAsync(other, movement.Id, Ct));
        Assert.True(await store.DeleteMovementAsync(userId, movement.Id, Ct));
        Assert.Empty(await store.ListMovementsAsync(userId, Ct));
    }

    [Fact]
    public async Task A_week_keeps_its_first_plan()
    {
        var userId = await CreateUserAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWeeklyPlanStore>();
        var week = new DateOnly(2026, 10, 5);
        var priority = new PlanPriority(LeakStat.Vpip, PokerPosition.Button, LeakDirection.TooLow, LeakConfidence.Confirmed, 0.31m, 120, 0.4m, 0.6m);
        var first = new WeeklyPlan(week, TableFormat.FullRing, [priority], 2, new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

        await store.AddIfAbsentAsync(userId, first, Ct);
        await store.AddIfAbsentAsync(userId, first with { Priorities = [], CreatedAt = first.CreatedAt.AddHours(1) }, Ct);

        var stored = await store.GetAsync(userId, week, Ct);
        Assert.NotNull(stored);
        Assert.Equal(first.CreatedAt, stored.CreatedAt);
        Assert.Equal(priority, Assert.Single(stored.Priorities));
        Assert.Equal(week, Assert.Single(await store.ListBeforeAsync(userId, week.AddDays(7), 4, Ct)).WeekStart);

        await store.DeleteAsync(userId, week, Ct);
        Assert.Null(await store.GetAsync(userId, week, Ct));
    }

    [Fact]
    public async Task Breakdowns_split_the_hands_by_phase_and_month()
    {
        var userId = await ImportConfirmedAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var statistics = scope.ServiceProvider.GetRequiredService<StatisticsService>();

        var breakdowns = await statistics.GetBreakdownsAsync(userId, new StatisticsFilter(null, null, null, null), DateTimeOffset.UtcNow, Ct);

        Assert.Equal(222, breakdowns.ByPhase.Sum(p => p.Line.Hands));
        Assert.Contains(breakdowns.ByMonth, m => m.Month == new DateOnly(2026, 9, 1) && m.Line.Hands == 222);
        var early = await statistics.GetAsync(userId, new StatisticsFilter(null, null, null, null, Phase: TournamentPhase.Early), Ct);
        Assert.Equal(breakdowns.ByPhase.Single(p => p.Phase == TournamentPhase.Early).Line.Hands, early.Overall.Hands);
    }

    [Fact]
    public async Task Real_spots_answered_right_leave_the_quiz()
    {
        var userId = await ImportConfirmedAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var spots = scope.ServiceProvider.GetRequiredService<IRealSpotStore>();
        var training = scope.ServiceProvider.GetRequiredService<ITrainingStore>();
        var since = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var rows = await spots.ListOpeningSpotsAsync(userId, TableFormat.SixMax, since, HeroHandFacts.Version, Ct);
        Assert.NotEmpty(rows);
        var row = rows[0];
        var item = new DrillItem(TableFormat.SixMax, StackBand.Deep, PokerPosition.Button, RangeNotation.Parse("AKs").Single());
        var answeredAt = DateTimeOffset.UtcNow;

        await training.RecordAsync(userId, new DrillAttempt(item, DrillAnswer.Fold, false, answeredAt, row.HandId), ReferenceOpeningRanges.Version, Ct);
        Assert.False((await training.RealHandOutcomesAsync(userId, TableFormat.SixMax, Ct))[row.HandId]);
        await training.RecordAsync(userId, new DrillAttempt(item, DrillAnswer.Raise, true, answeredAt.AddSeconds(1), row.HandId), ReferenceOpeningRanges.Version, Ct);

        Assert.True((await training.RealHandOutcomesAsync(userId, TableFormat.SixMax, Ct))[row.HandId]);
        Assert.Equal((2, 1), await training.CountSinceAsync(userId, answeredAt.AddMinutes(-1), null, Ct));
        Assert.Equal((0, 0), await training.CountSinceAsync(userId, answeredAt.AddMinutes(1), null, Ct));
    }

    [Fact]
    public async Task Deleting_an_account_removes_everything_it_owns()
    {
        var userId = await ImportConfirmedAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAccountDataStore>();
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        await scope.ServiceProvider.GetRequiredService<IBankrollStore>()
            .SaveSettingsAsync(userId, new BankrollSettings(500m, start, BuyInRule.Standard), Ct);

        var snapshot = await store.GetSnapshotAsync(userId, Ct);
        Assert.NotNull(snapshot);
        Assert.Single(snapshot.PokerAccounts);
        var uploads = 0;
        await foreach (var _ in store.StreamUploadsAsync(userId, Ct))
        {
            uploads++;
        }

        Assert.Equal(2, uploads);

        Assert.True(await store.DeleteUserAsync(userId, Ct));
        Assert.Null(await store.GetSnapshotAsync(userId, Ct));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IBankrollStore>().GetSettingsAsync(userId, Ct));
        Assert.False(await store.DeleteUserAsync(userId, Ct));
    }

    [Fact]
    public async Task A_coach_report_is_replaced_in_place_exported_and_deleted_with_the_account()
    {
        var userId = await ImportConfirmedAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var reports = scope.ServiceProvider.GetRequiredService<ICoachingReportStore>();
        var created = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var moment = new DebriefMoment("M1", Guid.NewGuid(), 3, PokerPosition.Button, "AhAd", 22m, -22m, -1m, Domain.Tournaments.KeyMomentStage.Showdown, 0.81m);
        DebriefReport Report(string headline) =>
            new(new TournamentDebrief(headline, "story", [new MomentNote("M1", MomentVerdict.Variance, "note")], ["s"], ["w"]), [moment]);

        await reports.SaveAsync(userId, "tournament-debrief", "t1", "fr", new StoredReport<DebriefReport>(Report("first"), new string('a', 64), "m", created), Ct);
        await reports.SaveAsync(userId, "tournament-debrief", "t1", "fr", new StoredReport<DebriefReport>(Report("second"), new string('b', 64), "m", created.AddHours(1)), Ct);

        var stored = await reports.FindAsync<DebriefReport>(userId, "tournament-debrief", "t1", "fr", Ct);
        Assert.NotNull(stored);
        Assert.Equal("second", stored.Report.Debrief.Headline);
        Assert.Equal(new string('b', 64), stored.Fingerprint);
        Assert.Equal(moment, Assert.Single(stored.Report.Moments));
        Assert.Equal(MomentVerdict.Variance, Assert.Single(stored.Report.Debrief.Moments).Verdict);
        Assert.Null(await reports.FindAsync<DebriefReport>(userId, "tournament-debrief", "t1", "en", Ct));

        var accounts = scope.ServiceProvider.GetRequiredService<IAccountDataStore>();
        Assert.Single((await accounts.GetSnapshotAsync(userId, Ct))!.Reports!);
        Assert.True(await accounts.DeleteUserAsync(userId, Ct));
        Assert.Null(await reports.FindAsync<DebriefReport>(userId, "tournament-debrief", "t1", "fr", Ct));
    }

    [Fact]
    public async Task Key_moments_are_told_from_stored_hands_with_the_cards_shown()
    {
        var userId = await ImportConfirmedAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var tournamentId = (await scope.ServiceProvider.GetRequiredService<TournamentListService>()
            .ListAsync(userId, new TournamentFilter(null, null, null, null, 1, 10), Ct)).Items.Single().Id;
        var detail = await new TournamentDetailService(scope.ServiceProvider.GetRequiredService<ITournamentDetailStore>())
            .GetAsync(userId, tournamentId, Ct);
        Assert.NotNull(detail);
        Assert.NotEmpty(detail.KeyMoments);

        var hands = await scope.ServiceProvider.GetRequiredService<ICoachingReportStore>()
            .LoadHandsAsync(userId, detail.KeyMoments.Select(m => m.HandId).ToList(), Ct);
        var stories = hands.Select(HandNarrator.Tell).ToList();

        Assert.Equal(detail.KeyMoments.Count, hands.Count);
        Assert.Contains(stories, s => s.Contains("Shown: ", StringComparison.Ordinal));
        // Golden files name opponents Villain1, Villain2…: those pseudonyms must never reach the model.
        Assert.DoesNotContain(stories, s => System.Text.RegularExpressions.Regex.IsMatch(s, @"Villain\d|ACCELERATOR"));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ICoachingReportStore>().LoadHandsAsync(await CreateUserAsync(), [hands[0].HandId], Ct));

        var facts = TournamentDebriefService.Facts(detail, [], []);
        Assert.DoesNotContain("ACCELERATOR", facts, StringComparison.Ordinal);
        Assert.Contains("Played 222 hands", facts, StringComparison.Ordinal);
    }

    /// <summary>A user with the ACCELERATOR tournament imported, account confirmed and hand facts computed.</summary>
    private async Task<Guid> ImportConfirmedAsync()
    {
        var userId = await CreateUserAsync();
        await using (var upload = fixture.Services.CreateAsyncScope())
        {
            var sources = new[] { AcceleratorHands, AcceleratorSummary }
                .Select(name => new UploadSource(name, () => new MemoryStream(Golden(name))))
                .ToList();
            await upload.ServiceProvider.GetRequiredService<ImportService>().UploadAsync(userId, sources, Ct);
        }

        while (true)
        {
            await using var step = fixture.Services.CreateAsyncScope();
            if (!await step.ServiceProvider.GetRequiredService<ImportProcessor>().ProcessNextAsync(Ct))
            {
                break;
            }
        }

        await using var scope = fixture.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<PokerAccountService>();
        await accounts.ConfirmAsync(userId, Assert.Single(await accounts.ListAsync(userId, Ct)).Id, Ct);
        var facts = scope.ServiceProvider.GetRequiredService<HandFactsBackfill>();
        while (await facts.ProcessBatchAsync(Ct) > 0)
        {
        }

        return userId;
    }

    private async Task<Guid> CreateUserAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<ExternalSignInService>().SignInAsync(
            new ExternalSignIn(IdentityProviders.Google, Guid.NewGuid().ToString(), null, "Player", "fr"),
            Ct);
        return user.Id;
    }

    private static byte[] Golden(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Winamax", fileName));
}
