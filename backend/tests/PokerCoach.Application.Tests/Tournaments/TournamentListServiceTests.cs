using PokerCoach.Application.Tournaments;
using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Tournaments;

public sealed class TournamentListServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Totals_count_only_known_results_and_list_every_tournament()
    {
        var store = new StubStore(
            Facts("ACCELERATOR", 2m, 2.50m, 0.50m, [new EntryOutcome(11, 25.42m, 18.86m)]),
            Facts("ASTEROID", 2m, 2.50m, 0.50m, [new EntryOutcome(1151, null, null), new EntryOutcome(978, null, null)]),
            Facts("NO SUMMARY", null, null, 1m, [], buyInExcludingFee: 9m));

        var page = await new TournamentListService(store).ListAsync(UserId, Filter(), TestContext.Current.CancellationToken);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new TournamentTotals(3, 2, 3, 15m, 44.28m, 29.28m, 1.952m), page.Totals);
        var noSummary = page.Items.Single(i => i.Name == "NO SUMMARY");
        Assert.Equal(10m, noSummary.BuyIn);
        Assert.Equal(TournamentResultStatus.MissingSummary, noSummary.Result.Status);
        Assert.Equal(978, page.Items.Single(i => i.Name == "ASTEROID").FinishPosition);
    }

    [Fact]
    public async Task Buy_in_filter_and_paging_apply_before_and_after_totals()
    {
        var store = new StubStore(
            Facts("A", 2m, 2.50m, 0.50m, [new EntryOutcome(1, 100m, null)]),
            Facts("B", 4m, 5m, 1m, [new EntryOutcome(2, 50m, null)]),
            Facts("C", 4m, 5m, 1m, [new EntryOutcome(3, 20m, null)]));

        var page = await new TournamentListService(store).ListAsync(
            UserId,
            Filter() with { MinBuyIn = 10m, Page = 2, PageSize = 1 },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("C", Assert.Single(page.Items).Name);
        Assert.Equal(2, page.Totals.Tournaments);
        Assert.Equal(50m, page.Totals.Profit);
    }

    private static TournamentFilter Filter() => new(null, null, null, null, 1, 50);

    private static TournamentFacts Facts(
        string name,
        decimal? prizePool,
        decimal? bounty,
        decimal? fee,
        IReadOnlyList<EntryOutcome> entries,
        decimal? buyInExcludingFee = null) =>
        new(Guid.NewGuid(), name, DateTimeOffset.UnixEpoch, "EUR", prizePool, bounty, buyInExcludingFee, fee, 100, 10, entries);

    private sealed class StubStore(params TournamentFacts[] facts) : ITournamentReadStore
    {
        public Task<IReadOnlyList<TournamentFacts>> ListAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TournamentFacts>>(facts);
    }
}
