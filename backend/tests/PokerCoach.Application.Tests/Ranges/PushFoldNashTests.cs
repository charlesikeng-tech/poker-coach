using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Tests.Ranges;

public sealed class PushFoldNashTests
{
    private static HandClass Hand(string text) => RangeNotation.Parse(text).Single();

    [Theory]
    [InlineData("AA", "KK", 0.82)]
    [InlineData("QQ", "AKs", 0.54)]
    [InlineData("22", "AKo", 0.526)]
    [InlineData("AKo", "72o", 0.675)]
    public void The_equity_table_matches_published_figures(string hand, string other, double expected)
    {
        Assert.InRange(PreflopEquity.Of(Hand(hand), Hand(other)), expected - 0.01, expected + 0.01);
        Assert.InRange(PreflopEquity.Of(Hand(hand), Hand(other)) + PreflopEquity.Of(Hand(other), Hand(hand)), 0.999, 1.001);
    }

    [Fact]
    public void Card_removal_counts_the_holdings_left()
    {
        var aces = PreflopEquity.Index(Hand("AA"));
        var akOffsuit = PreflopEquity.Index(Hand("AKo"));

        Assert.Equal(6, PreflopEquity.Weight(akOffsuit, PreflopEquity.Index(Hand("QQ"))), 6);
        Assert.Equal(3, PreflopEquity.Weight(akOffsuit, aces), 6);
    }

    [Fact]
    public void Heads_up_at_10_bb_without_antes_matches_the_known_equilibrium()
    {
        // Known values: the small blind shoves about 58 % of hands, the big blind calls about 37 %.
        var chart = PushFoldNash.Solve(TableFormat.SixMax, 10, ante: 0m);

        Assert.InRange(ReferenceOpeningRanges.ComboShare(chart.Shove(PokerPosition.SmallBlind)), 0.55m, 0.61m);
        Assert.InRange(ReferenceOpeningRanges.ComboShare(chart.Call(PokerPosition.BigBlind, PokerPosition.SmallBlind)), 0.34m, 0.40m);
    }

    [Fact]
    public void Shoves_widen_towards_the_button_and_keep_the_obvious_hands()
    {
        var chart = PushFoldNash.Solve(TableFormat.SixMax, 10);
        var shares = new[] { PokerPosition.Utg, PokerPosition.Hijack, PokerPosition.Cutoff, PokerPosition.Button }
            .Select(p => ReferenceOpeningRanges.ComboShare(chart.Shove(p)))
            .ToList();

        Assert.Equal(shares.Order(), shares);
        Assert.Contains(Hand("AA"), chart.Shove(PokerPosition.Utg));
        Assert.DoesNotContain(Hand("72o"), chart.Shove(PokerPosition.Utg));
        Assert.Empty(chart.Shove(PokerPosition.BigBlind));
        Assert.Same(chart, PushFoldNash.Solve(TableFormat.SixMax, 10));
    }

    [Fact]
    public void Shorter_stacks_shove_wider()
    {
        var five = ReferenceOpeningRanges.ComboShare(PushFoldNash.Solve(TableFormat.SixMax, 5).Shove(PokerPosition.Button));
        var fifteen = ReferenceOpeningRanges.ComboShare(PushFoldNash.Solve(TableFormat.SixMax, 15).Shove(PokerPosition.Button));

        Assert.True(five > fifteen);
    }
}
