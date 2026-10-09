using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Bankroll;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Progress;
using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Bankroll;
using PokerCoach.Domain.Progress;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Identity;

public sealed class AccountDataServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_export_holds_the_uploads_as_sent_the_tournaments_and_the_account()
    {
        var store = new StubStore(
            Snapshot(),
            [
                new ExportedUpload("20260905_CASSIOPEIA.txt", Now.AddDays(-2), Gzip("Winamax Poker - hand 1")),
                new ExportedUpload("../../etc/passwd", Now.AddDays(-1), Gzip("not escaping")),
            ]);
        var service = Service(store, [Facts("SUNDAY, \"BIG\"", 5m, [new EntryOutcome(1, 120m, null)]), Facts("NO SUMMARY", 5m, [])]);
        using var output = new MemoryStream();

        var written = await service.WriteExportAsync(Guid.NewGuid(), output, TestContext.Current.CancellationToken);

        Assert.True(written);
        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("README.txt", names);
        Assert.Contains("account.json", names);
        Assert.Contains("tournaments.csv", names);
        Assert.Equal("Winamax Poker - hand 1", Read(zip, names.Single(n => n.EndsWith("CASSIOPEIA.txt", StringComparison.Ordinal))));
        Assert.All(names, n => Assert.DoesNotContain("..", n));

        var csv = Read(zip, "tournaments.csv").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, csv.Length);
        Assert.Contains("\"SUNDAY, \"\"BIG\"\"\"", csv[1] + csv[2]);
        // Unknown result: empty cells, never zero.
        Assert.EndsWith(",MissingSummary,,,,,", csv.Single(l => l.Contains("NO SUMMARY", StringComparison.Ordinal)).TrimEnd('\r'), StringComparison.Ordinal);

        using var account = JsonDocument.Parse(Read(zip, "account.json"));
        Assert.Equal("Charles", account.RootElement.GetProperty("account").GetProperty("displayName").GetString());
        Assert.Equal(1, account.RootElement.GetProperty("bankroll").GetProperty("movements").GetArrayLength());
    }

    [Fact]
    public async Task No_export_for_an_unknown_user()
    {
        var service = Service(new StubStore(null, []), []);
        using var output = new MemoryStream();

        Assert.False(await service.WriteExportAsync(Guid.NewGuid(), output, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }

    private static AccountDataService Service(StubStore store, TournamentFacts[] facts) =>
        new(store, new TournamentListService(new StubTournaments(facts)), new StubBankroll(), new NoPlans(), new FakeTimeProvider(Now));

    private static AccountSnapshot Snapshot() =>
        new("Charles", "c@example.com", "fr", Now.AddMonths(-1), [new ExportedIdentity("google", "123", Now.AddMonths(-1))], [new ExportedPokerAccount("Winamax", "Hero", Now, Now)], [], [new ExportedExplanation(Now, "fr", "sonnet", "{\"summary\":\"ok\"}")]);

    private static TournamentFacts Facts(string name, decimal buyIn, IReadOnlyList<EntryOutcome> entries) =>
        new(Guid.NewGuid(), name, Now.AddDays(-3), "EUR", null, null, buyIn, 0m, 100, 10, entries);

    private static byte[] Gzip(string text)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionMode.Compress))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return buffer.ToArray();
    }

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private sealed class StubStore(AccountSnapshot? snapshot, ExportedUpload[] uploads) : IAccountDataStore
    {
        public Task<AccountSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(snapshot);

        public async IAsyncEnumerable<ExportedUpload> StreamUploadsAsync(Guid userId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var upload in uploads)
            {
                await Task.Yield();
                yield return upload;
            }
        }

        public Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(snapshot is not null);
    }

    private sealed class StubTournaments(TournamentFacts[] facts) : ITournamentReadStore
    {
        public Task<IReadOnlyList<TournamentFacts>> ListAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TournamentFacts>>(facts);
    }

    private sealed class StubBankroll : IBankrollStore
    {
        public Task<BankrollSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<BankrollSettings?>(new BankrollSettings(500m, Now.AddMonths(-1), BuyInRule.Standard));

        public Task SaveSettingsAsync(Guid userId, BankrollSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<BankrollMovement>> ListMovementsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankrollMovement>>([new BankrollMovement(Guid.NewGuid(), BankrollMovementKind.Deposit, 100m, Now, null)]);

        public Task AddMovementAsync(Guid userId, BankrollMovement movement, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> DeleteMovementAsync(Guid userId, Guid movementId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NoPlans : IWeeklyPlanStore
    {
        public Task<WeeklyPlan?> GetAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken) => Task.FromResult<WeeklyPlan?>(null);

        public Task AddIfAbsentAsync(Guid userId, WeeklyPlan plan, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(Guid userId, DateOnly weekStart, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<WeeklyPlan>> ListBeforeAsync(Guid userId, DateOnly before, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WeeklyPlan>>([]);
    }
}
