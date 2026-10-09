using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Evaluation;

namespace PokerCoach.Application.Tests.Evaluation;

public sealed class HandEvaluatorTests
{
    [Fact]
    public void Categories_rank_in_poker_order()
    {
        string[] fromBest =
        [
            "9h Th Jh Qh Kh 2c 3d", // straight flush
            "7c 7d 7h 7s Ah 2c 3d", // quads
            "8c 8d 8h 2s 2h 4c 5d", // full house
            "2h 5h 9h Jh Kh 3c 3d", // flush
            "5c 6d 7h 8s 9h Kc Kd", // straight
            "Qc Qd Qh 2s 7h 9c Jd", // trips
            "Ac Ad Kh Ks 7h 2c 3d", // two pair
            "Ac Ad Kh Qs 7h 2c 3d", // pair
            "Ac Jd 9h 7s 5h 3c 2d", // high card
        ];

        var strengths = fromBest.Select(Strength).ToList();

        Assert.Equal(strengths.OrderByDescending(s => s).ToList(), strengths);
    }

    [Fact]
    public void The_wheel_is_the_lowest_straight() =>
        Assert.True(Strength("Ac 2d 3h 4s 5h Kc Kd") < Strength("2c 3d 4h 5s 6h Kc Kd"));

    [Fact]
    public void Kickers_break_ties_and_the_sixth_card_does_not()
    {
        Assert.True(Strength("Ac Ad Kh 9s 7h 3c 2d") > Strength("Ac Ad Qh Js 7h 3c 2d"));
        Assert.Equal(Strength("Ac Ad Kh Qs Jh 3c 2d"), Strength("Ac Ad Kh Qs Jh 4c 2d"));
    }

    [Fact]
    public void Two_sets_of_trips_make_a_full_house_and_three_pairs_play_the_best_two()
    {
        Assert.Equal(Strength("9c 9d 9h 5s 5h 5c 2d"), Strength("9c 9d 9h 5s 5h Kc 2d"));
        Assert.True(Strength("Kc Kd 9h 9s 4h 4c Ad") > Strength("Kc Kd 9h 9s 4h 4c Qd"));
    }

    [Fact]
    public void A_flush_uses_its_five_best_cards() =>
        Assert.True(Strength("Ah 9h 7h 5h 3h 2h Kc") > Strength("Kh 9h 7h 5h 3h 2h Ac"));

    private static int Strength(string cards)
    {
        var c = cards.Split(' ').Select(text => Card.TryParse(text, out var card) ? HandEvaluator.Index(card) : throw new FormatException(text)).ToArray();
        return HandEvaluator.Evaluate(c[0], c[1], c[2], c[3], c[4], c[5], c[6]);
    }
}
