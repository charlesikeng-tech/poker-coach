namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>
/// The usual range shorthand, comma-separated: "77" a pair, "55+" pairs from 55 up, "99-66" a run of pairs,
/// "AKs" / "AKo" one hand, "A9s+" the kicker climbing up to just below the high card (A9s…AKs),
/// "A5s-A2s" a run of kickers. Parsing is strict: reference data is ours, a typo must fail loudly.
/// </summary>
public static class RangeNotation
{
    private const string RankSymbols = "23456789TJQKA";

    public static IReadOnlySet<HandClass> Parse(string notation)
    {
        ArgumentNullException.ThrowIfNull(notation);

        var hands = new HashSet<HandClass>();
        foreach (var raw in notation.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var hand in Token(raw))
            {
                hands.Add(hand);
            }
        }

        return hands;
    }

    private static IEnumerable<HandClass> Token(string token)
    {
        if (token.Contains('-', StringComparison.Ordinal))
        {
            var parts = token.Split('-');
            if (parts.Length != 2)
            {
                throw Invalid(token);
            }

            var (from, to) = (Single(parts[0], token), Single(parts[1], token));
            if (from.IsPair != to.IsPair || from.Suited != to.Suited || (!from.IsPair && from.High != to.High))
            {
                throw Invalid(token);
            }

            var (top, bottom) = from.IsPair
                ? (Max(from.High, to.High), Min(from.High, to.High))
                : (Max(from.Low, to.Low), Min(from.Low, to.Low));
            for (var rank = bottom; rank <= top; rank++)
            {
                yield return from.IsPair ? new HandClass(rank, rank, false) : new HandClass(from.High, rank, from.Suited);
            }

            yield break;
        }

        var plus = token.EndsWith('+');
        var hand = Single(plus ? token[..^1] : token, token);
        if (!plus)
        {
            yield return hand;
            yield break;
        }

        if (hand.IsPair)
        {
            for (var rank = hand.High; rank <= Rank.Ace; rank++)
            {
                yield return new HandClass(rank, rank, false);
            }
        }
        else
        {
            for (var rank = hand.Low; rank < hand.High; rank++)
            {
                yield return new HandClass(hand.High, rank, hand.Suited);
            }
        }
    }

    private static HandClass Single(string text, string token)
    {
        if (text.Length is < 2 or > 3)
        {
            throw Invalid(token);
        }

        var first = RankSymbols.IndexOf(text[0], StringComparison.Ordinal);
        var second = RankSymbols.IndexOf(text[1], StringComparison.Ordinal);
        if (first < 0 || second < 0)
        {
            throw Invalid(token);
        }

        var high = (Rank)(Math.Max(first, second) + (int)Rank.Two);
        var low = (Rank)(Math.Min(first, second) + (int)Rank.Two);
        if (high == low)
        {
            return text.Length == 2 ? new HandClass(high, low, false) : throw Invalid(token);
        }

        return text.Length == 3 && text[2] is 's' or 'o'
            ? new HandClass(high, low, text[2] == 's')
            : throw Invalid(token);
    }

    private static Rank Max(Rank a, Rank b) => a > b ? a : b;

    private static Rank Min(Rank a, Rank b) => a < b ? a : b;

    private static FormatException Invalid(string token) => new($"Invalid range token '{token}'.");
}
