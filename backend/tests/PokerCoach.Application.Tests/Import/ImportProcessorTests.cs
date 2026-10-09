using System.Text;
using PokerCoach.Application.Import;
using PokerCoach.Domain.Poker;
using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.Application.Tests.Import;

public sealed class ImportProcessorTests
{
    private const string CassiopeiaHands = "20260905_CASSIOPEIA_1161415333__real_holdem_no-limit.txt";
    private const string CassiopeiaSummary = "20260905_CASSIOPEIA_1161415333__real_holdem_no-limit_summary.txt";

    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryImportStore store = new();
    private readonly ImportProcessor processor;

    public ImportProcessorTests() =>
        processor = new ImportProcessor(store, [new WinamaxHandHistoryProvider()], new ImportOptions());

    [Fact]
    public async Task Hands_are_imported_under_the_hero_account_and_tournament()
    {
        var fileId = store.Enqueue(UserId, Golden(CassiopeiaHands));

        Assert.True(await ProcessAsync());

        var outcome = store.Outcomes[fileId];
        Assert.Equal(ImportFileStatus.Completed, outcome.Status);
        Assert.Equal(ImportFileKind.HandHistory, outcome.Kind);
        Assert.Equal(72, outcome.HandsImported);
        Assert.Equal(0, outcome.HandsRejected);
        var account = Assert.Single(store.Accounts);
        Assert.Equal((UserId, PokerRoom.Winamax, "Hero"), account.Key);
        Assert.Equal(account.Value, outcome.PokerAccountId);
        Assert.Equal("1161415333", Assert.Single(store.Tournaments).Key.ExternalId);
    }

    [Fact]
    public async Task Importing_the_same_hands_again_adds_nothing()
    {
        store.Enqueue(UserId, Golden(CassiopeiaHands));
        var secondId = store.Enqueue(UserId, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Golden(CassiopeiaHands)) + "\n"));

        await ProcessAsync();
        await ProcessAsync();

        var outcome = store.Outcomes[secondId];
        Assert.Equal(ImportFileStatus.Completed, outcome.Status);
        Assert.Equal(0, outcome.HandsImported);
        Assert.Equal(72, outcome.HandsAlreadyPresent);
        Assert.Equal(72, store.Hands.Count);
    }

    [Fact]
    public async Task A_summary_completes_the_tournament_of_the_same_account()
    {
        store.Enqueue(UserId, Golden(CassiopeiaHands));
        var summaryId = store.Enqueue(UserId, Golden(CassiopeiaSummary));

        await ProcessAsync();
        await ProcessAsync();

        Assert.Equal(ImportFileKind.TournamentSummary, store.Outcomes[summaryId].Kind);
        Assert.Single(store.Accounts);
        Assert.Single(store.Tournaments);
        Assert.Equal(108, Assert.Single(Assert.Single(store.Summaries).Value.Entries).FinishPosition);
    }

    [Fact]
    public async Task A_truncated_file_imports_its_complete_hands_and_reports_the_last_one()
    {
        var content = Encoding.UTF8.GetString(Golden(CassiopeiaHands));
        var fileId = store.Enqueue(UserId, Encoding.UTF8.GetBytes(content[..content.LastIndexOf("*** SUMMARY ***", StringComparison.Ordinal)]));

        await ProcessAsync();

        var outcome = store.Outcomes[fileId];
        Assert.Equal(ImportFileStatus.Completed, outcome.Status);
        Assert.Equal(71, outcome.HandsImported);
        Assert.Equal(1, outcome.HandsRejected);
        Assert.Single(outcome.Rejections);
    }

    [Fact]
    public async Task Invalid_utf8_fails_the_file()
    {
        var fileId = store.Enqueue(UserId, [0x57, 0x69, 0xC3, 0x28]);

        await ProcessAsync();

        Assert.Equal(ImportErrorCodes.InvalidEncoding, store.Outcomes[fileId].ErrorCode);
        Assert.Equal(ImportFileStatus.Failed, store.Outcomes[fileId].Status);
    }

    [Fact]
    public async Task An_unknown_format_fails_the_file()
    {
        var fileId = store.Enqueue(UserId, Encoding.UTF8.GetBytes("PokerStars Hand #1: Tournament #2\n"));

        await ProcessAsync();

        Assert.Equal(ImportErrorCodes.UnrecognizedFormat, store.Outcomes[fileId].ErrorCode);
        Assert.Empty(store.Accounts);
    }

    [Fact]
    public async Task An_unexpected_error_gives_the_file_back_and_propagates()
    {
        var fileId = store.Enqueue(UserId, Golden(CassiopeiaHands));
        store.FailOnHandInsert = new TimeoutException();

        await Assert.ThrowsAsync<TimeoutException>(ProcessAsync);

        Assert.Equal([fileId], store.Released);
        Assert.Empty(store.Outcomes);
    }

    [Fact]
    public async Task No_waiting_file_means_no_work() => Assert.False(await ProcessAsync());

    [Fact]
    public void A_byte_order_mark_is_not_part_of_the_text()
    {
        Assert.True(ImportProcessor.TryDecode([0xEF, 0xBB, 0xBF, 0x41], out var text));
        Assert.Equal("A", text);
    }

    private Task<bool> ProcessAsync() => processor.ProcessNextAsync(TestContext.Current.CancellationToken);

    private static byte[] Golden(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Winamax", fileName));
}
