using PokerCoach.Domain.Tournaments;

namespace PokerCoach.Application.Tests.Tournaments;

public sealed class TournamentResultTests
{
    [Fact]
    public void Profit_is_prize_plus_bounty_minus_every_buy_in()
    {
        // ACCELERATOR: 5 € entry, 11th, 25.42 € + 18.86 € bounty.
        var result = TournamentResult.Compute(5m, [new EntryOutcome(11, 25.42m, 18.86m)]);

        Assert.Equal(new TournamentResult(TournamentResultStatus.Known, 1, 5m, 25.42m, 18.86m, 39.28m, PaidEntries: 1), result);
    }

    [Fact]
    public void A_re_entry_counts_a_second_buy_in()
    {
        // ASTEROID: two 5 € entries, no money.
        var result = TournamentResult.Compute(5m, [new EntryOutcome(1151, null, null), new EntryOutcome(978, null, null)]);

        Assert.Equal(TournamentResultStatus.Known, result.Status);
        Assert.Equal(10m, result.TotalBuyIn);
        Assert.Equal(-10m, result.Profit);
    }

    [Fact]
    public void Nothing_printed_with_a_finish_position_means_nothing_won()
    {
        var result = TournamentResult.Compute(5m, [new EntryOutcome(185, null, 1m)]);

        Assert.Equal(0m, result.PrizeWinnings);
        Assert.Equal(1m, result.BountyWinnings);
        Assert.Equal(-4m, result.Profit);
        Assert.Equal(0, result.PaidEntries);
    }

    [Fact]
    public void Without_a_summary_the_result_is_unknown_not_zero()
    {
        var result = TournamentResult.Compute(10m, []);

        Assert.Equal(TournamentResultStatus.MissingSummary, result.Status);
        Assert.Null(result.Entries);
        Assert.Null(result.Profit);
    }

    [Fact]
    public void A_missing_finish_position_without_prize_is_not_assumed_zero()
    {
        var result = TournamentResult.Compute(10m, [new EntryOutcome(null, null, null)]);

        Assert.Equal(TournamentResultStatus.Incomplete, result.Status);
        Assert.Null(result.Profit);
    }

    [Fact]
    public void An_unknown_buy_in_makes_the_result_incomplete()
    {
        var result = TournamentResult.Compute(null, [new EntryOutcome(3, 50m, null)]);

        Assert.Equal(TournamentResultStatus.Incomplete, result.Status);
        Assert.Equal(1, result.Entries);
    }
}
