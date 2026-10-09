namespace PokerCoach.Domain.Poker.Evaluation;

/// <summary>
/// Best five-card hand out of seven, as a strength where a higher value wins and equal values tie.
/// Bit arithmetic only (no lookup table): fast enough for equity sampling, and small enough to read.
/// Cards are indices 0–51: rank (0 = deuce … 12 = ace) × 4 + suit.
/// </summary>
public static class HandEvaluator
{
    private enum Category
    {
        HighCard,
        Pair,
        TwoPair,
        Trips,
        Straight,
        Flush,
        FullHouse,
        Quads,
        StraightFlush,
    }

    public static int Index(Card card) => (((int)card.Rank - (int)Rank.Two) * 4) + (int)card.Suit;

    /// <summary>Strength of the best hand in exactly seven distinct cards.</summary>
    public static int Evaluate(int c0, int c1, int c2, int c3, int c4, int c5, int c6)
    {
        Span<int> suitMasks = stackalloc int[4];
        Span<byte> counts = stackalloc byte[13];
        Add(c0, suitMasks, counts);
        Add(c1, suitMasks, counts);
        Add(c2, suitMasks, counts);
        Add(c3, suitMasks, counts);
        Add(c4, suitMasks, counts);
        Add(c5, suitMasks, counts);
        Add(c6, suitMasks, counts);

        for (var suit = 0; suit < 4; suit++)
        {
            var flush = suitMasks[suit];
            if (int.PopCount(flush) >= 5)
            {
                var straightFlush = StraightHigh(flush);
                return straightFlush >= 0
                    ? Score(Category.StraightFlush, straightFlush)
                    : Score(Category.Flush, TopRanks(flush, 5));
            }
        }

        int quads = -1, trips = -1, pairHigh = -1, pairLow = -1, ranks = 0;
        for (var rank = 12; rank >= 0; rank--)
        {
            switch (counts[rank])
            {
                case 4:
                    quads = rank;
                    break;
                case 3 when trips < 0:
                    trips = rank;
                    break;
                case 3 or 2 when pairHigh < 0:
                    // A second set of trips plays as the pair of a full house.
                    pairHigh = rank;
                    break;
                case 3 or 2 when pairLow < 0:
                    pairLow = rank;
                    break;
            }

            if (counts[rank] > 0)
            {
                ranks |= 1 << rank;
            }
        }

        if (quads >= 0)
        {
            return Score(Category.Quads, (quads << 4) | HighestBit(ranks & ~(1 << quads)));
        }

        if (trips >= 0 && pairHigh >= 0)
        {
            return Score(Category.FullHouse, (trips << 4) | pairHigh);
        }

        var straight = StraightHigh(ranks);
        if (straight >= 0)
        {
            return Score(Category.Straight, straight);
        }

        if (trips >= 0)
        {
            return Score(Category.Trips, (trips << 8) | TopRanks(ranks & ~(1 << trips), 2));
        }

        if (pairHigh >= 0 && pairLow >= 0)
        {
            var kicker = HighestBit(ranks & ~(1 << pairHigh) & ~(1 << pairLow));
            return Score(Category.TwoPair, (pairHigh << 8) | (pairLow << 4) | kicker);
        }

        if (pairHigh >= 0)
        {
            return Score(Category.Pair, (pairHigh << 12) | TopRanks(ranks & ~(1 << pairHigh), 3));
        }

        return Score(Category.HighCard, TopRanks(ranks, 5));
    }

    private static void Add(int card, Span<int> suitMasks, Span<byte> counts)
    {
        var rank = card >> 2;
        suitMasks[card & 3] |= 1 << rank;
        counts[rank]++;
    }

    private static int Score(Category category, int tiebreak) => ((int)category << 24) | tiebreak;

    /// <summary>High card of the best straight in a rank mask, -1 if none. The wheel (A-5) is 3 (the five).</summary>
    private static int StraightHigh(int ranks)
    {
        // Shift up one and put the ace below the deuce, so A-2-3-4-5 is five bits in a row.
        var bits = (ranks << 1) | ((ranks >> 12) & 1);
        var runs = bits & (bits << 1) & (bits << 2) & (bits << 3) & (bits << 4);
        return runs == 0 ? -1 : HighestBit(runs) - 1;
    }

    /// <summary>The <paramref name="count"/> highest ranks of a mask, packed four bits each, highest first.</summary>
    private static int TopRanks(int ranks, int count)
    {
        var packed = 0;
        for (var i = 0; i < count; i++)
        {
            var top = HighestBit(ranks);
            packed = (packed << 4) | top;
            ranks &= ~(1 << top);
        }

        return packed;
    }

    private static int HighestBit(int mask) => 31 - int.LeadingZeroCount(mask);
}
