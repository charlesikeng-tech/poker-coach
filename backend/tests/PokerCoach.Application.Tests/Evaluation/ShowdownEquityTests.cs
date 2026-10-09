using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Evaluation;

namespace PokerCoach.Application.Tests.Evaluation;

public sealed class ShowdownEquityTests
{
    [Theory]
    [InlineData("Ah", "As", "Kh", "Ks", 0.8264)]
    [InlineData("Ah", "Kh", "Qs", "Qd", 0.4621)]
    [InlineData("7h", "2c", "Ad", "Ks", 0.3300)]
    [InlineData("Ah", "Kh", "Ad", "Kd", 0.5000)]
    public void Heads_up_preflop_equity_matches_known_values(string a1, string a2, string b1, string b2, double expected)
    {
        var shares = ShowdownEquity.Compute([(C(a1), C(a2)), (C(b1), C(b2))], [], [new[] { 0, 1 }]);

        Assert.Equal(expected, shares[0][0], 3);
        Assert.Equal(1.0, shares[0][0] + shares[0][1], 9);
    }

    [Fact]
    public void A_complete_board_settles_the_pot()
    {
        var shares = ShowdownEquity.Compute(
            [(C("Ah"), C("Ad")), (C("Kh"), C("Kd"))],
            [C("Kc"), C("7s"), C("2d"), C("9h"), C("3c")],
            [new[] { 0, 1 }]);

        Assert.Equal(0.0, shares[0][0]);
        Assert.Equal(1.0, shares[0][1]);
    }

    [Fact]
    public void A_side_pot_is_shared_only_by_its_players()
    {
        // The short stack (player 0) has aces but only plays for the main pot.
        var shares = ShowdownEquity.Compute(
            [(C("Ac"), C("Ad")), (C("Kh"), C("Ks")), (C("Qh"), C("Qs"))],
            [],
            [new[] { 0, 1, 2 }, new[] { 1, 2 }]);

        Assert.Equal(1.0, shares[0].Sum(), 9);
        Assert.Equal(0.0, shares[1][0]);
        Assert.True(shares[1][1] > 0.75, $"KK vs QQ {shares[1][1]}");
        Assert.True(shares[0][0] > 0.6, $"AA three-way {shares[0][0]}");
    }

    [Fact]
    public void The_same_card_twice_is_refused() =>
        Assert.Throws<ArgumentException>(() => ShowdownEquity.Compute([(C("Ah"), C("Kd")), (C("Ah"), C("Qs"))], [], [new[] { 0, 1 }]));

    private static Card C(string text) => Card.TryParse(text, out var card) ? card : throw new FormatException(text);
}
