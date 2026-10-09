using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Import;
using PokerCoach.Application.Poker;
using PokerCoach.Application.Statistics;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Tournaments;
using PokerCoach.Domain.Identity;
using PokerCoach.Infrastructure.Import;
using PokerCoach.Infrastructure.Persistence;
using PokerCoach.Infrastructure.Poker;

namespace PokerCoach.IntegrationTests;

public sealed class ImportPipelineTests(PostgresFixture fixture)
{
    private const string CassiopeiaHands = "20260905_CASSIOPEIA_1161415333__real_holdem_no-limit.txt";
    private const string CassiopeiaSummary = "20260905_CASSIOPEIA_1161415333__real_holdem_no-limit_summary.txt";
    private const string AcceleratorHands = "20260916_ACCELERATOR_1169257027__real_holdem_no-limit.txt";
    private const string AcceleratorSummary = "20260916_ACCELERATOR_1169257027__real_holdem_no-limit_summary.txt";
    private const string AsteroidSummary = "20261003_ASTEROID_1178140542__real_holdem_no-limit_summary.txt";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_uploaded_file_is_processed_into_hands_of_the_hero_account()
    {
        var userId = await CreateUserAsync();

        var upload = await UploadAsync(userId, (CassiopeiaHands, Golden(CassiopeiaHands)));
        Assert.True(upload.HasQueuedFiles);
        await ProcessQueueAsync();

        var file = Assert.Single(await BatchAsync(userId, upload.BatchId));
        Assert.Equal(ImportFileStatus.Completed, file.Status);
        Assert.Equal(ImportFileKind.HandHistory, file.Kind);
        Assert.Equal("Hero", file.ScreenName);
        Assert.Equal(72, file.HandsImported);
        Assert.Equal(0, file.HandsRejected);
        Assert.Equal(72, await CountHandsAsync(userId));

        var account = Assert.Single(await AccountsAsync(userId));
        Assert.Null(account.ConfirmedAt);
    }

    [Fact]
    public async Task The_same_content_uploaded_twice_is_stored_once()
    {
        var userId = await CreateUserAsync();
        await UploadAsync(userId, (CassiopeiaHands, Golden(CassiopeiaHands)));
        await ProcessQueueAsync();

        var second = await UploadAsync(userId, ("renamed.txt", Golden(CassiopeiaHands)));

        Assert.False(second.HasQueuedFiles);
        var file = Assert.Single(second.Files);
        Assert.True(file.AlreadyImported);
        Assert.Equal(ImportFileStatus.Completed, file.Status);
        Assert.Equal(1, await CountFilesAsync(userId));
    }

    [Fact]
    public async Task A_file_still_being_written_then_complete_ends_with_every_hand_once()
    {
        var userId = await CreateUserAsync();
        var full = Encoding.UTF8.GetString(Golden(CassiopeiaHands));
        var truncated = full[..full.LastIndexOf("*** SUMMARY ***", StringComparison.Ordinal)];

        var first = await UploadAsync(userId, (CassiopeiaHands, Encoding.UTF8.GetBytes(truncated)));
        await ProcessQueueAsync();
        var second = await UploadAsync(userId, (CassiopeiaHands, Golden(CassiopeiaHands)));
        await ProcessQueueAsync();

        var partial = Assert.Single(await BatchAsync(userId, first.BatchId));
        Assert.Equal(71, partial.HandsImported);
        Assert.Equal(1, partial.HandsRejected);
        var complete = Assert.Single(await BatchAsync(userId, second.BatchId));
        Assert.Equal(1, complete.HandsImported);
        Assert.Equal(71, complete.HandsAlreadyPresent);
        Assert.Equal(72, await CountHandsAsync(userId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hands_and_summary_complete_one_tournament_in_either_order(bool summaryFirst)
    {
        var userId = await CreateUserAsync();
        (string, byte[]) hands = (CassiopeiaHands, Golden(CassiopeiaHands));
        (string, byte[]) summary = (CassiopeiaSummary, Golden(CassiopeiaSummary));

        await UploadAsync(userId, summaryFirst ? summary : hands);
        await ProcessQueueAsync();
        await UploadAsync(userId, summaryFirst ? hands : summary);
        await ProcessQueueAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>();
        var tournament = await (
            from t in db.Set<TournamentRecord>()
            join a in db.Set<PokerAccountRecord>() on t.PokerAccountId equals a.Id
            where a.UserId == userId
            select t).SingleAsync(Ct);
        Assert.Equal("1161415333", tournament.ExternalTournamentId);
        Assert.NotNull(tournament.SummaryImportedAt);
        var entry = await db.Set<TournamentEntryRecord>().SingleAsync(e => e.TournamentId == tournament.Id, Ct);
        Assert.Equal(108, entry.FinishPosition);
        Assert.NotNull(tournament.FirstHandAt);
        Assert.NotNull(tournament.BuyInExcludingFee);
    }

    [Fact]
    public async Task A_summary_grown_by_a_re_entry_replaces_the_entries()
    {
        var userId = await CreateUserAsync();
        var full = Encoding.UTF8.GetString(Golden(AsteroidSummary));
        var firstEntryOnly = full[..full.IndexOf("Winamax Poker", 10, StringComparison.Ordinal)];

        await UploadAsync(userId, (AsteroidSummary, Encoding.UTF8.GetBytes(firstEntryOnly)));
        await ProcessQueueAsync();
        await UploadAsync(userId, (AsteroidSummary, Golden(AsteroidSummary)));
        await ProcessQueueAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>();
        var tournament = await (
            from t in db.Set<TournamentRecord>()
            join a in db.Set<PokerAccountRecord>() on t.PokerAccountId equals a.Id
            where a.UserId == userId
            select t).SingleAsync(Ct);
        Assert.Equal(1795, tournament.RegisteredPlayers);
        var entries = await db.Set<TournamentEntryRecord>()
            .Where(e => e.TournamentId == tournament.Id)
            .OrderBy(e => e.EntryNumber)
            .Select(e => e.FinishPosition)
            .ToListAsync(Ct);
        Assert.Equal(new int?[] { 1151, 978 }, entries);
    }

    [Fact]
    public async Task Tournaments_count_once_the_account_is_confirmed()
    {
        var userId = await CreateUserAsync();
        await UploadAsync(
            userId,
            (AcceleratorHands, Golden(AcceleratorHands)),
            (AcceleratorSummary, Golden(AcceleratorSummary)),
            (AsteroidSummary, Golden(AsteroidSummary)),
            (CassiopeiaHands, Golden(CassiopeiaHands)));
        await ProcessQueueAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var tournaments = scope.ServiceProvider.GetRequiredService<TournamentListService>();
        var filter = new TournamentFilter(null, null, null, null, 1, 50);
        Assert.Equal(0, (await tournaments.ListAsync(userId, filter, Ct)).TotalCount);

        var accounts = scope.ServiceProvider.GetRequiredService<PokerAccountService>();
        await accounts.ConfirmAsync(userId, Assert.Single(await accounts.ListAsync(userId, Ct)).Id, Ct);
        var page = await tournaments.ListAsync(userId, filter, Ct);

        // ACCELERATOR: 5 € in, 44.28 € back. ASTEROID: two 5 € entries, nothing back.
        // CASSIOPEIA: hands only, no summary: listed, not counted.
        Assert.Equal(new PerformanceFigures(3, 2, 3, 1, 15m, 44.28m, 29.28m, 1.952m, 0.3333m), page.Totals);
        var accelerator = page.Items.Single(i => i.Name == "ACCELERATOR");
        Assert.Equal(222, accelerator.HandCount);
        Assert.Equal(11, accelerator.FinishPosition);
        Assert.Equal(TournamentResultStatus.MissingSummary, page.Items.Single(i => i.Name == "CASSIOPEIA").Result.Status);
        Assert.Equal(new[] { "ASTEROID", "ACCELERATOR", "CASSIOPEIA" }, page.Items.Select(i => i.Name).ToArray());

        var performance = await scope.ServiceProvider.GetRequiredService<PerformanceService>().GetAsync(userId, null, null, Ct);
        Assert.Equal(page.Totals, performance.Totals);
        Assert.Equal(new[] { 39.28m, 29.28m }, performance.Curve.Select(p => p.CumulativeProfit));
        Assert.Equal(new[] { "upTo5", "from5To10" }, performance.ByBuyIn.Select(g => g.Key));
    }

    [Fact]
    public async Task Statistics_appear_once_hand_facts_are_computed()
    {
        var userId = await CreateUserAsync();
        await UploadAsync(userId, (CassiopeiaHands, Golden(CassiopeiaHands)));
        await ProcessQueueAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<PokerAccountService>();
        await accounts.ConfirmAsync(userId, Assert.Single(await accounts.ListAsync(userId, Ct)).Id, Ct);
        var statistics = scope.ServiceProvider.GetRequiredService<StatisticsService>();
        var noFilter = new StatisticsFilter(null, null, null, null);

        var before = await statistics.GetAsync(userId, noFilter, Ct);
        Assert.Equal(72, before.PendingHands);
        Assert.Equal(0, before.Overall.Hands);

        // What the background worker does when the import queue is empty.
        var backfill = scope.ServiceProvider.GetRequiredService<HandFactsBackfill>();
        while (await backfill.ProcessBatchAsync(Ct) > 0)
        {
        }

        var after = await statistics.GetAsync(userId, noFilter, Ct);
        Assert.Equal(0, after.PendingHands);
        Assert.Equal(72, after.Overall.Hands);
        Assert.Equal(72, after.ByPosition.Sum(p => p.Line.Hands));
        Assert.InRange(after.Overall.Vpip.Opportunities, 1, 72);
        Assert.True(after.Overall.Pfr.Made <= after.Overall.Vpip.Made);

        var deep = await statistics.GetAsync(userId, noFilter with { MinStackBigBlinds = 1_000m }, Ct);
        Assert.Equal(0, deep.Overall.Hands);
        Assert.Equal(0, await backfill.ProcessBatchAsync(Ct));
    }

    [Fact]
    public async Task A_failed_file_is_processed_again_when_uploaded_again()
    {
        var userId = await CreateUserAsync();
        byte[] notUtf8 = [0x57, 0x69, 0xC3, 0x28];

        var first = await UploadAsync(userId, ("bad.txt", notUtf8));
        await ProcessQueueAsync();
        Assert.Equal(ImportErrorCodes.InvalidEncoding, Assert.Single(await BatchAsync(userId, first.BatchId)).ErrorCode);

        var second = await UploadAsync(userId, ("bad.txt", notUtf8));

        Assert.False(Assert.Single(second.Files).AlreadyImported);
        Assert.Equal(ImportFileStatus.Pending, Assert.Single(await BatchAsync(userId, second.BatchId)).Status);
        await ProcessQueueAsync();
    }

    [Fact]
    public async Task Concurrent_workers_never_process_a_file_twice()
    {
        var userId = await CreateUserAsync();
        var full = Encoding.UTF8.GetString(Golden(AcceleratorHands));
        var upload = await UploadAsync(
            userId,
            (AcceleratorHands, Golden(AcceleratorHands)),
            (CassiopeiaHands, Golden(CassiopeiaHands)),
            ("accelerator-copy.txt", Encoding.UTF8.GetBytes(full + "\n")));

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(ProcessQueueAsync, Ct)));

        var files = await BatchAsync(userId, upload.BatchId);
        Assert.All(files, f => Assert.Equal(ImportFileStatus.Completed, f.Status));
        Assert.Equal(222 + 72, files.Sum(f => f.HandsImported));
        Assert.Equal(222, files.Sum(f => f.HandsAlreadyPresent));
        Assert.Equal(222 + 72, await CountHandsAsync(userId));
    }

    [Fact]
    public async Task Deleting_an_account_removes_its_data_and_allows_a_new_upload()
    {
        var userId = await CreateUserAsync();
        await UploadAsync(userId, (CassiopeiaHands, Golden(CassiopeiaHands)), (CassiopeiaSummary, Golden(CassiopeiaSummary)));
        await ProcessQueueAsync();
        var account = Assert.Single(await AccountsAsync(userId));

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<PokerAccountService>().DeleteAsync(userId, account.Id, Ct));
        }

        Assert.Equal(0, await CountHandsAsync(userId));
        Assert.Equal(0, await CountFilesAsync(userId));
        Assert.True((await UploadAsync(userId, (CassiopeiaHands, Golden(CassiopeiaHands)))).HasQueuedFiles);
        await ProcessQueueAsync();
    }

    [Fact]
    public async Task Confirming_an_account_is_idempotent_and_limited_to_its_owner()
    {
        var owner = await CreateUserAsync();
        var stranger = await CreateUserAsync();
        await UploadAsync(owner, (CassiopeiaSummary, Golden(CassiopeiaSummary)));
        await ProcessQueueAsync();
        var account = Assert.Single(await AccountsAsync(owner));

        await using var scope = fixture.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<PokerAccountService>();
        Assert.False(await accounts.ConfirmAsync(stranger, account.Id, Ct));
        Assert.False(await accounts.DeleteAsync(stranger, account.Id, Ct));
        Assert.True(await accounts.ConfirmAsync(owner, account.Id, Ct));
        var confirmedAt = Assert.Single(await accounts.ListAsync(owner, Ct)).ConfirmedAt;
        Assert.True(await accounts.ConfirmAsync(owner, account.Id, Ct));
        Assert.Equal(confirmedAt, Assert.Single(await accounts.ListAsync(owner, Ct)).ConfirmedAt);
    }

    [Fact]
    public async Task A_batch_is_visible_to_its_owner_only()
    {
        var owner = await CreateUserAsync();
        var upload = await UploadAsync(owner, (CassiopeiaSummary, Golden(CassiopeiaSummary)));

        Assert.Empty(await BatchAsync(await CreateUserAsync(), upload.BatchId));
        await ProcessQueueAsync();
    }

    [Fact]
    public async Task Concurrent_first_sign_ins_create_one_account()
    {
        var subject = Guid.NewGuid().ToString();

        var users = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            async () =>
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ExternalSignInService>().SignInAsync(
                    new ExternalSignIn(IdentityProviders.Google, subject, "race@example.com", "Race", "fr"),
                    Ct);
            },
            Ct)));

        Assert.Single(users.Select(u => u.Id).Distinct());
        await using var check = fixture.Services.CreateAsyncScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<PokerCoachDbContext>()
            .Set<ExternalIdentity>().CountAsync(i => i.ProviderSubjectId == subject, Ct));
    }

    private async Task<Guid> CreateUserAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<ExternalSignInService>().SignInAsync(
            new ExternalSignIn(IdentityProviders.Google, Guid.NewGuid().ToString(), null, "Player", "fr"),
            Ct);
        return user.Id;
    }

    private async Task<UploadResult> UploadAsync(Guid userId, params (string Name, byte[] Content)[] files)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var sources = files.Select(f => new UploadSource(f.Name, () => new MemoryStream(f.Content))).ToList();
        return await scope.ServiceProvider.GetRequiredService<ImportService>().UploadAsync(userId, sources, Ct);
    }

    /// <summary>What the background worker does, run inline until the queue is empty.</summary>
    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<ImportProcessor>().ProcessNextAsync(Ct))
            {
                return;
            }
        }
    }

    private async Task<IReadOnlyList<ImportedFileView>> BatchAsync(Guid userId, Guid batchId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ImportService>().GetBatchAsync(userId, batchId, Ct);
    }

    private async Task<IReadOnlyList<PokerAccountView>> AccountsAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PokerAccountService>().ListAsync(userId, Ct);
    }

    private async Task<int> CountHandsAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>();
        return await (
            from h in db.Set<HandRecord>()
            join a in db.Set<PokerAccountRecord>() on h.PokerAccountId equals a.Id
            where a.UserId == userId
            select h).CountAsync(Ct);
    }

    private async Task<int> CountFilesAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PokerCoachDbContext>()
            .Set<ImportedFileRecord>().CountAsync(f => f.UserId == userId, Ct);
    }

    private static byte[] Golden(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Winamax", fileName));
}
