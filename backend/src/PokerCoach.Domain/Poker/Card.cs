namespace PokerCoach.Domain.Poker;

public enum Rank
{
    Two = 2,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King,
    Ace,
}

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

/// <summary>A playing card. Text form is rank then suit, lowercase suit: "Ah", "Tc", "2d".</summary>
public readonly record struct Card(Rank Rank, Suit Suit)
{
    private const string RankSymbols = "23456789TJQKA";
    private const string SuitSymbols = "cdhs";

    public static bool TryParse(string? text, out Card card)
    {
        card = default;
        if (text is null || text.Length != 2)
        {
            return false;
        }

        var rankIndex = RankSymbols.IndexOf(text[0], StringComparison.Ordinal);
        var suitIndex = SuitSymbols.IndexOf(text[1], StringComparison.Ordinal);
        if (rankIndex < 0 || suitIndex < 0)
        {
            return false;
        }

        card = new Card((Rank)(rankIndex + (int)Rank.Two), (Suit)suitIndex);
        return true;
    }

    public override string ToString() =>
        new(new[] { RankSymbols[(int)Rank - (int)Rank.Two], SuitSymbols[(int)Suit] });
}
