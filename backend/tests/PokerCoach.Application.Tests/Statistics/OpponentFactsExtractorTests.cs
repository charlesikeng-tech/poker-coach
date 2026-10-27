using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using static PokerCoach.Domain.Poker.Analysis.ActionKind;

namespace PokerCoach.Application.Tests.Statistics;

public sealed class OpponentFactsExtractorTests
{
    private static readonly string[] SixMax = ["P1", "P2", "P3", "P4", "P5", "Hero"];

    [Fact]
    public void An_opponent_who_raises_first_in_has_vpip_and_pfr()
    {
        var facts = Extract(
            Blinds(),
            Pre("P3", Raise, 500), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Fold), Pre("P1", Fold), Pre("P2", Fold));

        Assert.True(facts["P3"].Vpip);
        Assert.True(facts["P3"].Pfr);
        Assert.True(facts["P3"].HadDecision);
        Assert.False(facts["P3"].ThreeBetOpportunity);
    }

    [Fact]
    public void An_opponent_who_calls_has_vpip_but_not_pfr()
    {
        var facts = Extract(
            Blinds(),
            Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Raise, 500), Pre("P1", Fold), Pre("P2", Call, 300));

        Assert.True(facts["P2"].Vpip);
        Assert.False(facts["P2"].Pfr);
    }

    [Fact]
    public void An_opponent_who_3bets_hero_has_3bet_opportunity_and_3bet()
    {
        var facts = Extract(
            Blinds(),
            Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Raise, 500), Pre("P1", Raise, 1400), Pre("P2", Fold),
            Pre("Hero", Fold));

        Assert.True(facts["P1"].ThreeBetOpportunity);
        Assert.True(facts["P1"].ThreeBet);
        Assert.True(facts["P1"].Vpip);
        Assert.True(facts["P1"].Pfr);
    }

    [Fact]
    public void An_opponent_facing_one_raise_who_folds_has_3bet_opportunity_but_no_3bet()
    {
        var facts = Extract(
            Blinds(),
            Pre("P3", Raise, 500), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Fold), Pre("P1", Fold), Pre("P2", Fold));

        Assert.True(facts["P4"].HadDecision);
        Assert.True(facts["P4"].ThreeBetOpportunity);
        Assert.False(facts["P4"].ThreeBet);
        Assert.False(facts["P4"].Vpip);
    }

    [Fact]
    public void An_opponent_who_folds_preflop_does_not_see_the_flop()
    {
        var facts = Extract(
            Blinds(),
            Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Raise, 500), Pre("P1", Fold), Pre("P2", Call, 300),
            new HandAction(Street.Flop, "P2", Check, 0, false),
            new HandAction(Street.Flop, "Hero", Bet, 400, false),
            new HandAction(Street.Flop, "P2", Fold, 0, false));

        Assert.False(facts["P3"].SawFlop);
        Assert.True(facts["P2"].SawFlop);
        Assert.False(facts["P2"].WentToShowdown);
    }

    [Fact]
    public void Postflop_aggression_counts_bets_and_raises()
    {
        var facts = Extract(
            Blinds(),
            Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Raise, 500), Pre("P1", Fold), Pre("P2", Call, 300),
            new HandAction(Street.Flop, "P2", Check, 0, false),
            new HandAction(Street.Flop, "Hero", Bet, 400, false),
            new HandAction(Street.Flop, "P2", Raise, 800, false),
            new HandAction(Street.Flop, "Hero", Call, 800, false),
            new HandAction(Street.Turn, "P2", Bet, 1000, false),
            new HandAction(Street.Turn, "Hero", Fold, 0, false));

        Assert.Equal(2, facts["P2"].PostflopAggressive);
        Assert.True(facts["P2"].PostflopDecisions >= 3);
    }

    [Fact]
    public void Showdown_is_counted_when_opponent_remains_until_the_end()
    {
        var facts = Extract(
            Blinds(),
            Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Raise, 500), Pre("P1", Fold), Pre("P2", Call, 300),
            new HandAction(Street.Flop, "P2", Check, 0, false),
            new HandAction(Street.Flop, "Hero", Bet, 400, false),
            new HandAction(Street.Flop, "P2", Call, 400, false),
            new HandAction(Street.Turn, "P2", Check, 0, false),
            new HandAction(Street.Turn, "Hero", Check, 0, false),
            new HandAction(Street.River, "P2", Check, 0, false),
            new HandAction(Street.River, "Hero", Check, 0, false));

        Assert.True(facts["P2"].SawFlop);
        Assert.True(facts["P2"].WentToShowdown);
    }

    [Fact]
    public void Hero_is_never_in_the_opponent_facts()
    {
        var facts = Extract(
            Blinds(),
            Pre("P3", Raise, 500), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Call, 500), Pre("P1", Fold), Pre("P2", Fold));

        Assert.False(facts.ContainsKey("Hero"));
    }

    [Fact]
    public void Players_who_only_post_blinds_and_get_walked_are_excluded()
    {
        var facts = Extract(
            [
                .. SixMax.Select(p => new HandAction(Street.Preflop, p, PostAnte, 25, false)),
                new HandAction(Street.Preflop, "P1", PostSmallBlind, 100, false),
                new HandAction(Street.Preflop, "P2", PostBigBlind, 200, false),
            ],
            Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Fold), Pre("P1", Fold));

        Assert.DoesNotContain("P2", facts.Keys);
    }

    private static IReadOnlyDictionary<string, OpponentHandFacts> Extract(HandAction[] forced, params HandAction[] actions) =>
        OpponentFactsExtractor.Extract(new HandForAnalysis(
            6,
            200,
            "Hero",
            SixMax.Select((p, i) => new SeatState(i + 1, p, 20_000)).ToList(),
            [.. forced, .. actions],
            new Dictionary<string, long>()));

    private static HandAction Pre(string player, ActionKind kind, long amount = 0) =>
        new(Street.Preflop, player, kind, amount, false);

    private static HandAction[] Blinds() =>
    [
        .. SixMax.Select(p => new HandAction(Street.Preflop, p, PostAnte, 25, false)),
        new HandAction(Street.Preflop, "P1", PostSmallBlind, 100, false),
        new HandAction(Street.Preflop, "P2", PostBigBlind, 200, false),
    ];
}
