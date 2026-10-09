using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using static PokerCoach.Domain.Poker.Analysis.ActionKind;

namespace PokerCoach.Application.Tests.Statistics;

public sealed class PostflopPlayTests
{
    // Heads-up to the flop: the opener (Villain or Hero) and the caller.
    private static readonly string[] Players = ["Villain", "Hero"];

    [Fact]
    public void Folding_to_the_openers_flop_bet_is_a_fold_to_cbet()
    {
        var facts = Analyze(
            Pre("Villain", Raise, 500), Pre("Hero", Call, 500),
            Flop("Hero", Check), Flop("Villain", Bet, 400), Flop("Hero", Fold));

        Assert.True(facts.FoldToCbetFlopOpportunity);
        Assert.True(facts.FoldToCbetFlop);
        Assert.False(facts.RaiseCbetFlop);
        // Checking then facing a bet is also a check-raise spot, not taken.
        Assert.True(facts.CheckRaiseFlopOpportunity);
        Assert.False(facts.CheckRaiseFlop);
        Assert.Equal(1, facts.PostflopDecisions);
        Assert.Equal(0, facts.PostflopAggressive);
    }

    [Fact]
    public void Check_raising_the_cbet_counts_as_a_raise_and_a_check_raise()
    {
        var facts = Analyze(
            Pre("Villain", Raise, 500), Pre("Hero", Call, 500),
            Flop("Hero", Check), Flop("Villain", Bet, 400), Flop("Hero", Raise, 1200), Flop("Villain", Fold));

        Assert.True(facts.RaiseCbetFlop);
        Assert.True(facts.CheckRaiseFlop);
        Assert.False(facts.FoldToCbetFlop);
        Assert.Equal(1, facts.PostflopAggressive);
    }

    [Fact]
    public void A_bet_from_the_caller_is_not_a_cbet_the_hero_faces()
    {
        var facts = Analyze(
            Pre("Hero", Raise, 500), Pre("Villain", Call, 500),
            Flop("Villain", Bet, 400), Flop("Hero", Fold));

        Assert.False(facts.FoldToCbetFlopOpportunity);
    }

    [Fact]
    public void Betting_the_turn_after_a_called_cbet_is_a_second_barrel()
    {
        var barrel = Analyze(
            Pre("Hero", Raise, 500), Pre("Villain", Call, 500),
            Flop("Villain", Check), Flop("Hero", Bet, 400), Flop("Villain", Call, 400),
            Turn("Villain", Check), Turn("Hero", Bet, 1000), Turn("Villain", Fold));
        var giveUp = Analyze(
            Pre("Hero", Raise, 500), Pre("Villain", Call, 500),
            Flop("Villain", Check), Flop("Hero", Bet, 400), Flop("Villain", Call, 400),
            Turn("Villain", Check), Turn("Hero", Check));

        Assert.True(barrel.CbetFlop && barrel.CbetTurnOpportunity && barrel.CbetTurn);
        Assert.True(giveUp.CbetTurnOpportunity);
        Assert.False(giveUp.CbetTurn);
        Assert.Equal(2, barrel.PostflopAggressive);
    }

    [Fact]
    public void No_second_barrel_spot_after_a_raised_cbet_or_a_lead_on_the_turn()
    {
        var raised = Analyze(
            Pre("Hero", Raise, 500), Pre("Villain", Call, 500),
            Flop("Villain", Check), Flop("Hero", Bet, 400), Flop("Villain", Raise, 1200), Flop("Hero", Call, 800),
            Turn("Villain", Check), Turn("Hero", Bet, 1500));
        var lead = Analyze(
            Pre("Hero", Raise, 500), Pre("Villain", Call, 500),
            Flop("Villain", Check), Flop("Hero", Bet, 400), Flop("Villain", Call, 400),
            Turn("Villain", Bet, 1000), Turn("Hero", Fold));

        Assert.False(raised.CbetTurnOpportunity);
        Assert.False(lead.CbetTurnOpportunity);
    }

    [Fact]
    public void Winning_chips_after_seeing_the_flop_counts_even_without_showdown()
    {
        var facts = Analyze(
            [("Hero", 1400)],
            Pre("Villain", Raise, 500), Pre("Hero", Call, 500),
            Flop("Villain", Check), Flop("Hero", Bet, 400), Flop("Villain", Fold));

        Assert.True(facts.SawFlop);
        Assert.True(facts.WonWhenSawFlop);
        Assert.False(facts.WentToShowdown);
    }

    private static HandAction Pre(string player, ActionKind kind, long amount = 0) => new(Street.Preflop, player, kind, amount, false);

    private static HandAction Flop(string player, ActionKind kind, long amount = 0) => new(Street.Flop, player, kind, amount, false);

    private static HandAction Turn(string player, ActionKind kind, long amount = 0) => new(Street.Turn, player, kind, amount, false);

    private static HeroHandFacts Analyze(params HandAction[] actions) => Analyze([], actions);

    private static HeroHandFacts Analyze((string Player, long Amount)[] collected, params HandAction[] actions) =>
        HandAnalyzer.Analyze(new HandForAnalysis(
            1,
            200,
            "Hero",
            Players.Select((p, i) => new SeatState(i + 1, p, 20_000)).ToList(),
            [
                new HandAction(Street.Preflop, "Hero", PostSmallBlind, 100, false),
                new HandAction(Street.Preflop, "Villain", PostBigBlind, 200, false),
                .. actions,
            ],
            collected.ToDictionary(c => c.Player, c => c.Amount)));
}
