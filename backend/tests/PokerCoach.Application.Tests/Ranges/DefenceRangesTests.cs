using PokerCoach.Application.Ranges;
using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Tests.Ranges;

public sealed class ReferenceDefenceRangesTests
{
    private static decimal Share(DefenceReference r) => ReferenceOpeningRanges.ComboShare(r.Call) + ReferenceOpeningRanges.ComboShare(r.ThreeBet);

    [Fact]
    public void No_hand_is_both_called_and_3bet_and_premiums_are_always_3bet()
    {
        Assert.All(ReferenceDefenceRanges.All, r =>
        {
            Assert.Empty(r.Call.Intersect(r.ThreeBet));
            Assert.Contains(r.ThreeBet, h => h.ToString() == "AA");
            Assert.Contains(r.ThreeBet, h => h.ToString() == "AKs");
        });
    }

    [Fact]
    public void The_big_blind_defends_wider_as_the_opener_gets_later()
    {
        var shares = new[] { PokerPosition.Utg, PokerPosition.Hijack, PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind }
            .Select(o => Share(ReferenceDefenceRanges.For(StackBand.Deep, TableFormat.FullRing, PokerPosition.BigBlind, o)!))
            .ToList();

        Assert.Equal(shares.Order(), shares);
        Assert.InRange(shares[0], 0.15m, 0.35m);
        Assert.InRange(shares[3], 0.45m, 0.75m);
    }

    [Fact]
    public void References_cover_25_bb_and_more_and_go_by_distance_to_the_button()
    {
        Assert.Null(ReferenceDefenceRanges.For(StackBand.Short, TableFormat.FullRing, PokerPosition.BigBlind, PokerPosition.Button));
        Assert.Null(ReferenceDefenceRanges.For(StackBand.Push, TableFormat.FullRing, PokerPosition.BigBlind, PokerPosition.Button));
        Assert.Same(
            ReferenceDefenceRanges.For(StackBand.Mid, TableFormat.FullRing, PokerPosition.BigBlind, PokerPosition.Button),
            ReferenceDefenceRanges.For(StackBand.Deep, TableFormat.FullRing, PokerPosition.BigBlind, PokerPosition.Button));

        // A 6-max UTG opens like a full-ring lojack: the big blind defends against a middle seat.
        Assert.Same(
            ReferenceDefenceRanges.For(StackBand.Mid, TableFormat.FullRing, PokerPosition.BigBlind, PokerPosition.Lojack),
            ReferenceDefenceRanges.For(StackBand.Mid, TableFormat.SixMax, PokerPosition.BigBlind, PokerPosition.Utg));

        // An early seat facing an earlier open has no reference written.
        Assert.Null(ReferenceDefenceRanges.For(StackBand.Mid, TableFormat.FullRing, PokerPosition.Utg2, PokerPosition.Utg));
    }
}

public sealed class DefenceRangeServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Counts_are_summed_per_defender_opener_and_hand_next_to_the_reference()
    {
        var store = new Store(
            new DefenceHoldingCount(PokerPosition.BigBlind, PokerPosition.Button, 6, 6, "AhKd", 3, 0, 3),
            new DefenceHoldingCount(PokerPosition.BigBlind, PokerPosition.Button, 6, 6, "KsAc", 1, 1, 0),
            new DefenceHoldingCount(PokerPosition.BigBlind, PokerPosition.Button, 6, 6, "7h2c", 4, 0, 0),
            new DefenceHoldingCount(PokerPosition.BigBlind, PokerPosition.Cutoff, 6, 6, "QsJs", 2, 2, 0),
            // Unreadable: left out, not guessed. Another format: not in this view.
            new DefenceHoldingCount(PokerPosition.BigBlind, PokerPosition.Button, 6, 6, "??", 5, 5, 0),
            new DefenceHoldingCount(PokerPosition.BigBlind, PokerPosition.Button, 9, 9, "AhKd", 7, 7, 0));

        var result = await new DefenceRangeService(store, new NoPending()).GetAsync(Guid.NewGuid(), TableFormat.SixMax, StackBand.Mid, null, null, null, null, Ct);

        // 6-max: UTG faced by four seats, then the hijack by three… 15 pairs.
        Assert.Equal(15, result.Pairs.Count);
        var selected = result.Selected;
        Assert.Equal((PokerPosition.BigBlind, PokerPosition.Button), (selected.Pair.Defender, selected.Pair.Opener));
        Assert.Equal((8, 1, 3), (selected.Pair.Dealt, selected.Pair.Calls, selected.Pair.ThreeBets));
        Assert.Equal(0.5m, selected.Pair.DefenceRate.Rate);

        var ako = selected.Cells.Single(c => c.Hand.ToString() == "AKo");
        Assert.Equal((4, 1, 3, DefenceAction.ThreeBet), (ako.Dealt, ako.Calls, ako.ThreeBets, ako.Reference));
        Assert.Equal(DefenceAction.Fold, selected.Cells.Single(c => c.Hand.ToString() == "72o").Reference);
        Assert.NotNull(selected.CallNotation);
        Assert.Equal((15, 7), (result.SpotsByFormat[TableFormat.SixMax], result.SpotsByFormat[TableFormat.FullRing]));
    }

    [Fact]
    public async Task Below_25_bb_the_spot_has_counts_but_no_reference()
    {
        var store = new Store(new DefenceHoldingCount(PokerPosition.SmallBlind, PokerPosition.Button, 6, 6, "AhKd", 1, 0, 1));

        var result = await new DefenceRangeService(store, new NoPending()).GetAsync(
            Guid.NewGuid(), TableFormat.SixMax, StackBand.Short, PokerPosition.SmallBlind, PokerPosition.Button, null, null, Ct);

        Assert.Equal(PokerPosition.SmallBlind, result.Selected.Pair.Defender);
        Assert.False(result.Selected.Pair.HasReference);
        Assert.All(result.Selected.Cells, c => Assert.Null(c.Reference));
        Assert.Null(result.Selected.CallShare);
    }

    private sealed class Store(params DefenceHoldingCount[] counts) : IRangeReadStore
    {
        public Task<IReadOnlyList<DefenceHoldingCount>> CountDefencesAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DefenceHoldingCount>>(counts);

        public Task<IReadOnlyList<OpeningHoldingCount>> CountOpeningsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoPending : IStatisticsReadStore
    {
        public Task<int> CountPendingAsync(Guid userId, int factsVersion, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyList<(PokerPosition? Position, HeroStatCounts Counts)>> CountByPositionAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatisticsSample> CountTournamentsAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FormatCounts> CountByFormatAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<(DateOnly Month, HeroStatCounts Counts)>> CountByMonthAsync(Guid userId, StatisticsFilter filter, int factsVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
