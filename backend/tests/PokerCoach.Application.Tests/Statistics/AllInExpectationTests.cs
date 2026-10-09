using PokerCoach.Application.Statistics;
using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Analysis;
using PokerCoach.Domain.Poker.Evaluation;
using PokerCoach.HandHistories.Winamax;

namespace PokerCoach.Application.Tests.Statistics;

public sealed class AllInExpectationTests
{
    [Fact]
    public void Heads_up_shove_and_call_is_worth_the_pot_times_the_equity_minus_the_stake()
    {
        var hand = Hand(
            [
                new HandAction(Street.Preflop, "Hero", ActionKind.PostSmallBlind, 50, false),
                new HandAction(Street.Preflop, "Villain", ActionKind.PostBigBlind, 100, false),
                new HandAction(Street.Preflop, "Hero", ActionKind.Raise, 1950, true),
                new HandAction(Street.Preflop, "Villain", ActionKind.Call, 1900, false),
            ],
            collected: new() { ["Villain"] = 4000 },
            known: new() { ["Hero"] = ("Ah", "As"), ["Villain"] = ("Kh", "Ks") });

        var outcome = AllInExpectation.Compute(hand)!;

        Assert.Equal(0.8264m, outcome.Equity, 3);
        // Equity is shown rounded to 0.01 %; the expectation uses the exact share.
        Assert.InRange(outcome.ExpectedNetChips, (4000m * outcome.Equity) - 2001m, (4000m * outcome.Equity) - 1999m);
        Assert.Equal(-2000, HandAnalyzer.Analyze(hand).NetChips);
        Assert.Equal(outcome.ExpectedNetChips, HandAnalyzer.Analyze(hand).AllInExpectedNetChips);
    }

    [Fact]
    public void The_unmatched_part_of_a_shove_is_given_back_before_the_pot_is_shared()
    {
        var hand = Hand(
            [
                new HandAction(Street.Preflop, "Villain", ActionKind.PostBigBlind, 100, false),
                new HandAction(Street.Preflop, "Hero", ActionKind.Raise, 5000, true),
                new HandAction(Street.Preflop, "Villain", ActionKind.Call, 1100, true),
            ],
            collected: new() { ["Hero"] = 6200 },
            known: new() { ["Hero"] = ("Ah", "Kh"), ["Villain"] = ("Qs", "Qd") });

        var outcome = AllInExpectation.Compute(hand)!;

        // 3,800 back for sure; 2,400 shared at about 46 %.
        Assert.InRange(outcome.ExpectedNetChips, 3800m + (2400m * outcome.Equity) - 5001m, 3800m + (2400m * outcome.Equity) - 4999m);
    }

    [Fact]
    public void Side_pots_count_only_for_the_players_who_paid_into_them()
    {
        var contributions = new Dictionary<string, long> { ["Short"] = 1000, ["Hero"] = 3000, ["Big"] = 3000, ["Folder"] = 200 };

        var (pots, returned) = AllInExpectation.BuildPots(contributions, ["Big", "Hero", "Short"]);

        Assert.Empty(returned);
        Assert.Equal(new[] { 3200L, 4000L }, pots.Select(p => p.Amount));
        Assert.Equal(new[] { "Big", "Hero", "Short" }, pots[0].Eligible);
        Assert.Equal(new[] { "Big", "Hero" }, pots[1].Eligible);
    }

    [Fact]
    public void Three_way_all_in_uses_the_side_pot_equity()
    {
        var hand = Hand(
            [
                new HandAction(Street.Preflop, "Short", ActionKind.Raise, 1000, true),
                new HandAction(Street.Preflop, "Hero", ActionKind.Raise, 3000, true),
                new HandAction(Street.Preflop, "Big", ActionKind.Call, 3000, false),
            ],
            collected: new() { ["Short"] = 3000, ["Big"] = 4000 },
            known: new() { ["Hero"] = ("Kh", "Ks"), ["Short"] = ("Ac", "Ad"), ["Big"] = ("Qh", "Qs") });

        var outcome = AllInExpectation.Compute(hand)!;
        var exact = ShowdownEquity.Compute(
            [(C("Qh"), C("Qs")), (C("Kh"), C("Ks")), (C("Ac"), C("Ad"))],
            [],
            [new[] { 0, 1, 2 }, new[] { 0, 1 }]);

        Assert.Equal((decimal)Math.Round(exact[0][1], 4), outcome.Equity);
        Assert.Equal(Math.Round((decimal)((3000 * exact[0][1]) + (4000 * exact[1][1])) - 3000m, 2), outcome.ExpectedNetChips);
    }

    [Fact]
    public void Not_counted_when_a_hand_was_not_shown()
    {
        var hand = Hand(
            [
                new HandAction(Street.Preflop, "Hero", ActionKind.Raise, 1000, true),
                new HandAction(Street.Preflop, "Villain", ActionKind.Call, 1000, false),
            ],
            collected: new() { ["Hero"] = 2000 },
            known: new() { ["Hero"] = ("Ah", "As") });

        Assert.Null(AllInExpectation.Compute(hand));
    }

    [Fact]
    public void Not_counted_when_betting_went_on_after_the_flop()
    {
        var hand = Hand(
            [
                new HandAction(Street.Preflop, "Short", ActionKind.Raise, 500, true),
                new HandAction(Street.Preflop, "Hero", ActionKind.Call, 500, false),
                new HandAction(Street.Preflop, "Big", ActionKind.Call, 500, false),
                new HandAction(Street.Flop, "Hero", ActionKind.Bet, 800, false),
                new HandAction(Street.Flop, "Big", ActionKind.Fold, 0, false),
            ],
            collected: new() { ["Hero"] = 2300 },
            known: new() { ["Hero"] = ("Ah", "As"), ["Short"] = ("Kh", "Ks") });

        Assert.Null(AllInExpectation.Compute(hand));
    }

    [Fact]
    public void Not_counted_when_the_hero_folded_or_nobody_was_all_in()
    {
        var folded = Hand(
            [
                new HandAction(Street.Preflop, "Villain", ActionKind.Raise, 1000, true),
                new HandAction(Street.Preflop, "Hero", ActionKind.Fold, 0, false),
            ],
            collected: new() { ["Villain"] = 1000 },
            known: new() { ["Hero"] = ("7h", "2c"), ["Villain"] = ("Ah", "As") });
        var noAllIn = Hand(
            [
                new HandAction(Street.Preflop, "Hero", ActionKind.Raise, 300, false),
                new HandAction(Street.Preflop, "Villain", ActionKind.Fold, 0, false),
            ],
            collected: new() { ["Hero"] = 300 },
            known: new() { ["Hero"] = ("Ah", "As") });

        Assert.Null(AllInExpectation.Compute(folded));
        Assert.Null(AllInExpectation.Compute(noAllIn));
    }

    [Fact]
    public void Not_counted_when_the_chips_do_not_add_up()
    {
        var hand = Hand(
            [
                new HandAction(Street.Preflop, "Hero", ActionKind.Raise, 1000, true),
                new HandAction(Street.Preflop, "Villain", ActionKind.Call, 1000, false),
            ],
            collected: new() { ["Hero"] = 1500 },
            known: new() { ["Hero"] = ("Ah", "As"), ["Villain"] = ("Kh", "Ks") });

        Assert.Null(AllInExpectation.Compute(hand));
    }

    [Fact]
    public void Real_hand_aces_against_queens_with_the_unmatched_shove_given_back()
    {
        // Equity is rounded to 0.01 %: ±3 chips on this pot.
        // ACCELERATOR, level 15: the hero calls 31,650 all-in with aces, the big blind covers him; the room
        // prints the 30,519 he could not win as a "side pot".
        var parsed = new WinamaxHandHistoryParser()
            .Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "Winamax", "20260916_ACCELERATOR_1169257027__real_holdem_no-limit.txt")))
            .Hands.Single(h => h.ExternalHandId == "5021920691583189000-86-1789521591");
        var hand = HandForAnalysisMapper.From(parsed)!;

        var outcome = AllInExpectation.Compute(hand)!;
        var facts = HandAnalyzer.Analyze(hand);

        Assert.InRange(outcome.Equity, 0.80m, 0.82m);
        Assert.InRange(outcome.ExpectedNetChips, (64_200m * outcome.Equity) - 31_655m, (64_200m * outcome.Equity) - 31_645m);
        Assert.Equal(32_550, facts.NetChips);
        Assert.Equal(13.02m, facts.NetBigBlinds);
    }

    private static HandForAnalysis Hand(
        IReadOnlyList<HandAction> actions,
        Dictionary<string, long> collected,
        Dictionary<string, (string First, string Second)> known)
    {
        var players = actions.Select(a => a.Player).Distinct().ToList();
        return new HandForAnalysis(
            1,
            100,
            "Hero",
            players.Select((p, i) => new SeatState(i + 1, p, 10_000)).ToList(),
            actions,
            collected,
            known.ToDictionary(k => k.Key, k => (C(k.Value.First), C(k.Value.Second))));
    }

    private static Card C(string text) => Card.TryParse(text, out var card) ? card : throw new FormatException(text);
}
