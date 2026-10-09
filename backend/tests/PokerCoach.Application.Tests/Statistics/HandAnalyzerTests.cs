using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.HandHistories.Winamax;
using static PokerCoach.Domain.Poker.Analysis.ActionKind;

namespace PokerCoach.Application.Tests.Statistics;

public sealed class HandAnalyzerTests
{
    // 6-max, seats 1..6, button on seat 6 (Hero): SB = P1, BB = P2, UTG = P3, HJ = P4, CO = P5.
    private static readonly string[] SixMax = ["P1", "P2", "P3", "P4", "P5", "Hero"];

    [Fact]
    public void An_open_from_the_button_is_an_rfi_and_a_steal_and_folding_to_the_3bet_is_counted()
    {
        var facts = Analyze(
            Blinds(),
            Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Raise, 500),
            Pre("P1", Fold),
            Pre("P2", Raise, 1400),
            Pre("Hero", Fold));

        Assert.Equal(PokerPosition.Button, facts.Position);
        Assert.True(facts.HadPreflopDecision);
        Assert.True(facts.Vpip && facts.Pfr);
        Assert.True(facts.RfiOpportunity && facts.Rfi);
        Assert.True(facts.StealOpportunity && facts.Steal);
        Assert.True(facts.FoldToThreeBetOpportunity && facts.FoldToThreeBet);
        Assert.False(facts.ThreeBetOpportunity);
        Assert.False(facts.SawFlop);
        Assert.Equal(-525, facts.NetChips);
    }

    [Fact]
    public void A_reraise_facing_one_raise_is_a_3bet_and_a_limp_spoils_the_rfi()
    {
        var threeBet = Analyze(Blinds(), Pre("P3", Raise, 400), Pre("P4", Fold), Pre("P5", Fold), Pre("Hero", Raise, 1200));
        Assert.True(threeBet.ThreeBetOpportunity && threeBet.ThreeBet);
        Assert.False(threeBet.RfiOpportunity);

        var afterLimp = Analyze(Blinds(), Pre("P3", Call, 200), Pre("P4", Fold), Pre("P5", Fold), Pre("Hero", Raise, 800));
        Assert.False(afterLimp.RfiOpportunity);
        Assert.False(afterLimp.ThreeBetOpportunity);
        Assert.True(afterLimp.Pfr);
    }

    [Fact]
    public void A_walk_gives_no_preflop_decision()
    {
        // Hero in the big blind (button on seat 4): everyone folds to him.
        var facts = Analyze(
            4,
            [new HandAction(Street.Preflop, "P5", PostSmallBlind, 100, false), new HandAction(Street.Preflop, "Hero", PostBigBlind, 200, false)],
            Pre("P1", Fold), Pre("P2", Fold), Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold));

        Assert.Equal(PokerPosition.BigBlind, facts.Position);
        Assert.False(facts.HadPreflopDecision);
        Assert.False(facts.Vpip);
    }

    [Fact]
    public void Betting_the_flop_as_the_preflop_raiser_when_checked_to_is_a_cbet()
    {
        var facts = Analyze(
            Blinds(),
            Pre("P3", Fold), Pre("P4", Fold), Pre("P5", Fold),
            Pre("Hero", Raise, 500), Pre("P1", Fold), Pre("P2", Call, 300),
            new HandAction(Street.Flop, "P2", Check, 0, false),
            new HandAction(Street.Flop, "Hero", Bet, 400, false),
            new HandAction(Street.Flop, "P2", Fold, 0, false));

        Assert.True(facts.SawFlop);
        Assert.True(facts.CbetFlopOpportunity && facts.CbetFlop);
        Assert.False(facts.WentToShowdown);
    }

    [Fact]
    public void Calling_down_to_a_showdown_won_counts_both_showdown_flags()
    {
        var facts = Analyze(
            Blinds(),
            [("Hero", 2200L)],
            Pre("P3", Raise, 400), Pre("P4", Fold), Pre("P5", Fold), Pre("Hero", Call, 600), Pre("P1", Fold), Pre("P2", Fold),
            new HandAction(Street.Flop, "P3", Bet, 500, false),
            new HandAction(Street.Flop, "Hero", Call, 500, false),
            new HandAction(Street.Turn, "P3", Check, 0, false),
            new HandAction(Street.Turn, "Hero", Check, 0, false),
            new HandAction(Street.River, "P3", Check, 0, false),
            new HandAction(Street.River, "Hero", Check, 0, false));

        Assert.True(facts.Vpip);
        Assert.False(facts.Pfr);
        Assert.False(facts.CbetFlopOpportunity);
        Assert.True(facts.WentToShowdown && facts.WonAtShowdown);
        Assert.Equal(2200 - 25 - 600 - 500, facts.NetChips);
    }

    [Fact]
    public void Positions_are_named_from_the_button_backwards()
    {
        string[] nine = ["A", "B", "C", "D", "E", "F", "G", "H", "Hero"];
        var hand = new HandForAnalysis(
            9,
            200,
            "Hero",
            nine.Select((p, i) => new SeatState(i + 1, p, 10_000)).ToList(),
            [
                .. nine.Select(p => new HandAction(Street.Preflop, p, PostAnte, 25, false)),
                new HandAction(Street.Preflop, "A", PostSmallBlind, 100, false),
                new HandAction(Street.Preflop, "B", PostBigBlind, 200, false),
            ],
            new Dictionary<string, long>());

        var positions = PositionResolver.Resolve(hand);

        Assert.Equal(PokerPosition.Utg, positions["C"]);
        Assert.Equal(PokerPosition.Utg1, positions["D"]);
        Assert.Equal(PokerPosition.Utg2, positions["E"]);
        Assert.Equal(PokerPosition.Lojack, positions["F"]);
        Assert.Equal(PokerPosition.Hijack, positions["G"]);
        Assert.Equal(PokerPosition.Cutoff, positions["H"]);
        Assert.Equal(PokerPosition.Button, positions["Hero"]);
    }

    [Fact]
    public void Real_hands_seated_not_dealt_and_folded_to()
    {
        var hands = new WinamaxHandHistoryParser().Parse(Golden("20260905_CASSIOPEIA_1161415333__real_holdem_no-limit.txt")).Hands;

        // Hand 1: Hero on the button, seat 3 seated but not dealt; a limper before him, Hero folds.
        var first = HandAnalyzer.Analyze(HandForAnalysisMapper.From(hands[0])!);
        Assert.Equal(PokerPosition.Button, first.Position);
        Assert.Equal(5, first.PlayersDealt);
        Assert.True(first.HadPreflopDecision);
        Assert.False(first.RfiOpportunity);
        Assert.False(first.Vpip);
        Assert.Equal(-160, first.NetChips);

        // Hand 2: Hero hijack (button seat 4), folded to him, folds.
        var second = HandAnalyzer.Analyze(HandForAnalysisMapper.From(hands[1])!);
        Assert.Equal(PokerPosition.Hijack, second.Position);
        Assert.True(second.RfiOpportunity);
        Assert.False(second.Rfi);
        Assert.False(second.StealOpportunity);
    }

    [Theory]
    [InlineData("20260905_CASSIOPEIA_1161415333__real_holdem_no-limit.txt")]
    [InlineData("20260916_ACCELERATOR_1169257027__real_holdem_no-limit.txt")]
    [InlineData("20261003_ASTEROID_1178140542__real_holdem_no-limit.txt")]
    public void Facts_of_every_real_hand_are_consistent(string file)
    {
        var hands = new WinamaxHandHistoryParser().Parse(Golden(file)).Hands;

        Assert.All(hands, hand =>
        {
            var input = HandForAnalysisMapper.From(hand)!;
            var facts = HandAnalyzer.Analyze(input);
            var positions = PositionResolver.Resolve(input);

            Assert.Equal(positions.Count, positions.Values.Distinct().Count());
            Assert.NotNull(facts.Position);
            Assert.True(!facts.Pfr || facts.Vpip);
            Assert.True(!facts.Vpip || facts.HadPreflopDecision);
            Assert.True(!facts.Rfi || facts.RfiOpportunity);
            Assert.True(!facts.Steal || (facts.StealOpportunity && facts.Rfi));
            Assert.True(!facts.ThreeBet || facts.ThreeBetOpportunity);
            Assert.True(!facts.FoldToThreeBet || facts.FoldToThreeBetOpportunity);
            Assert.True(!facts.CbetFlop || facts.CbetFlopOpportunity);
            Assert.True(!facts.WentToShowdown || facts.SawFlop);
            Assert.True(!facts.WonAtShowdown || facts.WentToShowdown);
            Assert.True(!facts.FoldToCbetFlop || facts.FoldToCbetFlopOpportunity);
            Assert.True(!facts.RaiseCbetFlop || facts.FoldToCbetFlopOpportunity);
            Assert.False(facts.FoldToCbetFlop && facts.RaiseCbetFlop);
            Assert.True(!facts.CbetTurn || (facts.CbetTurnOpportunity && facts.CbetFlop));
            Assert.True(!facts.CheckRaiseFlop || facts.CheckRaiseFlopOpportunity);
            Assert.True(!facts.WonWhenSawFlop || facts.SawFlop);
            Assert.InRange(facts.PostflopAggressive, 0, facts.PostflopDecisions);
            Assert.True(facts.SawFlop || facts.PostflopDecisions == 0);
        });

        // The hero can never win or lose more than the pot.
        Assert.All(hands, hand =>
            Assert.InRange(Math.Abs(HandAnalyzer.Analyze(HandForAnalysisMapper.From(hand)!).NetChips), 0, hand.TotalPot));
    }

    private static HandAction Pre(string player, ActionKind kind, long amount = 0) =>
        new(Street.Preflop, player, kind, amount, false);

    private static HandAction[] Blinds() =>
    [
        .. SixMax.Select(p => new HandAction(Street.Preflop, p, PostAnte, 25, false)),
        new HandAction(Street.Preflop, "P1", PostSmallBlind, 100, false),
        new HandAction(Street.Preflop, "P2", PostBigBlind, 200, false),
    ];

    private static HeroHandFacts Analyze(HandAction[] forced, params HandAction[] actions) =>
        Analyze(forced, [], actions);

    private static HeroHandFacts Analyze(HandAction[] forced, (string Player, long Amount)[] collected, params HandAction[] actions) =>
        HandAnalyzer.Analyze(new HandForAnalysis(
            6,
            200,
            "Hero",
            SixMax.Select((p, i) => new SeatState(i + 1, p, 20_000)).ToList(),
            [.. forced, .. actions],
            collected.ToDictionary(c => c.Player, c => c.Amount)));

    /// <summary>Button on <paramref name="buttonSeat"/>, antes from everyone plus the given blinds.</summary>
    private static HeroHandFacts Analyze(int buttonSeat, HandAction[] blinds, params HandAction[] actions) =>
        HandAnalyzer.Analyze(new HandForAnalysis(
            buttonSeat,
            200,
            "Hero",
            SixMax.Select((p, i) => new SeatState(i + 1, p, 20_000)).ToList(),
            [.. SixMax.Select(p => new HandAction(Street.Preflop, p, PostAnte, 25, false)), .. blinds, .. actions],
            new Dictionary<string, long>()));

    private static string Golden(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Winamax", fileName));
}
