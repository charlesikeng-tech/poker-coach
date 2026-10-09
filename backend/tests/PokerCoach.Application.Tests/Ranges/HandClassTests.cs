using PokerCoach.Domain.Poker;
using PokerCoach.Domain.Poker.Ranges;

namespace PokerCoach.Application.Tests.Ranges;

public sealed class HandClassTests
{
    [Theory]
    [InlineData("AhKh", "AKs")]
    [InlineData("KdAh", "AKo")]
    [InlineData("7c7d", "77")]
    [InlineData("2s3s", "32s")]
    [InlineData("Th9c", "T9o")]
    public void Holdings_map_to_their_class(string holdings, string expected)
    {
        Assert.True(HandClass.TryParseHoldings(holdings, out var hand));
        Assert.Equal(expected, hand.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AhKh9c")]
    [InlineData("AhAh")]
    [InlineData("XxKh")]
    public void Anything_else_is_not_a_hand(string? holdings)
    {
        Assert.False(HandClass.TryParseHoldings(holdings, out _));
    }

    [Fact]
    public void The_grid_has_169_hands_pairs_on_the_diagonal_suited_above()
    {
        var all = HandClass.All;

        Assert.Equal(169, all.Count);
        Assert.Equal(169, all.Distinct().Count());
        Assert.Equal(1326, all.Sum(h => h.Combinations));
        Assert.Equal("AA", all[0].ToString());
        Assert.Equal("AKs", all[1].ToString());
        Assert.Equal("AKo", all[13].ToString());
        Assert.Equal("22", all[168].ToString());
        for (var i = 0; i < all.Count; i++)
        {
            var (row, column) = all[i].GridCell;
            Assert.Equal(i, (row * 13) + column);
            Assert.Equal(row == column, all[i].IsPair);
            Assert.Equal(row < column, all[i].Suited);
        }
    }

    [Fact]
    public void Of_ignores_card_order()
    {
        var ace = new Card(Rank.Ace, Suit.Spades);
        var king = new Card(Rank.King, Suit.Spades);

        Assert.Equal(HandClass.Of(ace, king), HandClass.Of(king, ace));
    }
}
