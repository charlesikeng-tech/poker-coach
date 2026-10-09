namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>
/// One of the 169 starting hands: a pair, or two ranks suited or offsuit ("AA", "AKs", "AKo"). Suits are
/// forgotten, which is how ranges are read and learned.
/// </summary>
/// <param name="High">The higher rank (equal to <paramref name="Low"/> for a pair).</param>
public readonly record struct HandClass(Rank High, Rank Low, bool Suited)
{
    private const string RankSymbols = "23456789TJQKA";

    public bool IsPair => High == Low;

    /// <summary>Distinct two-card holdings of the class: 6 for a pair, 4 suited, 12 offsuit.</summary>
    public int Combinations => IsPair ? 6 : Suited ? 4 : 12;

    /// <summary>
    /// Place on the 13×13 grid, aces first: pairs on the diagonal, suited above it (row &lt; column),
    /// offsuit below.
    /// </summary>
    public (int Row, int Column) GridCell
    {
        get
        {
            var high = (int)Rank.Ace - (int)High;
            var low = (int)Rank.Ace - (int)Low;
            return IsPair || Suited ? (high, low) : (low, high);
        }
    }

    public static HandClass Of(Card first, Card second)
    {
        var (high, low) = first.Rank >= second.Rank ? (first, second) : (second, first);
        return new HandClass(high.Rank, low.Rank, high.Rank != low.Rank && high.Suit == low.Suit);
    }

    /// <summary>"AhKd" (two cards in text form) to its class; false for anything else.</summary>
    public static bool TryParseHoldings(string? text, out HandClass handClass)
    {
        handClass = default;
        if (text is not { Length: 4 }
            || !Card.TryParse(text[..2], out var first)
            || !Card.TryParse(text[2..], out var second)
            || first == second)
        {
            return false;
        }

        handClass = Of(first, second);
        return true;
    }

    /// <summary>The 169 classes in grid order (row by row, aces first).</summary>
    public static IReadOnlyList<HandClass> All { get; } = BuildAll();

    public override string ToString()
    {
        var text = $"{RankSymbols[(int)High - (int)Rank.Two]}{RankSymbols[(int)Low - (int)Rank.Two]}";
        return IsPair ? text : text + (Suited ? "s" : "o");
    }

    private static List<HandClass> BuildAll()
    {
        var all = new List<HandClass>(169);
        for (var row = Rank.Ace; row >= Rank.Two; row--)
        {
            for (var column = Rank.Ace; column >= Rank.Two; column--)
            {
                // Above the diagonal (row rank higher): suited; below: offsuit.
                all.Add(row == column
                    ? new HandClass(row, row, false)
                    : row > column
                        ? new HandClass(row, column, true)
                        : new HandClass(column, row, false));
            }
        }

        return all;
    }
}
