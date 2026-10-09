namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>
/// All-in equity of every starting hand against every other, and how many holdings of one hand remain
/// once another is dealt (card removal). Read from a table computed once (tools/preflop-equity); hands
/// are indexed in grid order (<see cref="HandClass.All"/>).
/// </summary>
public static class PreflopEquity
{
    public const int Hands = 169;

    private static readonly double[] Equities = Load();
    private static readonly double[] Weights = BuildWeights();

    /// <summary>Equity of <paramref name="hand"/> against <paramref name="other"/> (ties count half).</summary>
    public static double Of(HandClass hand, HandClass other) => Equities[(Index(hand) * Hands) + Index(other)];

    public static double Of(int hand, int other) => Equities[(hand * Hands) + other];

    /// <summary>
    /// Average number of holdings of <paramref name="other"/> still possible when one holding of
    /// <paramref name="hand"/> is dealt: AK given AA is 8 of 16, not 16.
    /// </summary>
    public static double Weight(int hand, int other) => Weights[(hand * Hands) + other];

    public static int Index(HandClass hand)
    {
        var (row, column) = hand.GridCell;
        return (row * 13) + column;
    }

    private static double[] Load()
    {
        using var stream = typeof(PreflopEquity).Assembly.GetManifestResourceStream("PokerCoach.Domain.preflop-equity.bin")
            ?? throw new InvalidOperationException("The preflop equity table is missing from the assembly.");
        using var reader = new BinaryReader(stream);
        var values = new double[Hands * Hands];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadUInt16() / 10_000d;
        }

        return values;
    }

    private static double[] BuildWeights()
    {
        var holdings = HandClass.All.Select(Holdings).ToArray();
        var weights = new double[Hands * Hands];
        for (var i = 0; i < Hands; i++)
        {
            for (var j = 0; j < Hands; j++)
            {
                var compatible = 0;
                foreach (var (a, b) in holdings[i])
                {
                    compatible += holdings[j].Count(h => h.Item1 != a && h.Item1 != b && h.Item2 != a && h.Item2 != b);
                }

                weights[(i * Hands) + j] = (double)compatible / holdings[i].Count;
            }
        }

        return weights;
    }

    private static List<(Card, Card)> Holdings(HandClass hand)
    {
        var holdings = new List<(Card, Card)>();
        var suits = Enum.GetValues<Suit>();
        foreach (var first in suits)
        {
            foreach (var second in suits)
            {
                var keep = hand.IsPair ? first < second : hand.Suited ? first == second : first != second;
                if (keep)
                {
                    holdings.Add((new Card(hand.High, first), new Card(hand.Low, second)));
                }
            }
        }

        return holdings;
    }
}
