using PokerCoach.Domain.Poker;

namespace PokerCoach.HandHistories.Tests.Domain;

public sealed class CardTests
{
    [Theory]
    [InlineData("Ah", Rank.Ace, Suit.Hearts)]
    [InlineData("Tc", Rank.Ten, Suit.Clubs)]
    [InlineData("2d", Rank.Two, Suit.Diamonds)]
    [InlineData("Ks", Rank.King, Suit.Spades)]
    public void Parses_and_prints_the_two_character_form(string text, Rank rank, Suit suit)
    {
        Assert.True(Card.TryParse(text, out var card));
        Assert.Equal(new Card(rank, suit), card);
        Assert.Equal(text, card.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("10h")]
    [InlineData("ah")]
    [InlineData("AH")]
    [InlineData("1c")]
    [InlineData("Ax")]
    public void Rejects_anything_else(string? text) => Assert.False(Card.TryParse(text, out _));
}
