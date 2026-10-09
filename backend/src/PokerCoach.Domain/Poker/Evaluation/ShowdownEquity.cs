namespace PokerCoach.Domain.Poker.Evaluation;

/// <summary>
/// Share of each pot every player can expect once all cards are dealt from a known starting point
/// (hole cards and the board so far). Every possible board is dealt: exact, and about 0.2 s for a
/// preflop all-in (1.7 million boards heads-up), which an import can afford for the few hands concerned.
/// </summary>
public static class ShowdownEquity
{
    /// <param name="holeCards">Two cards per player.</param>
    /// <param name="board">Cards already dealt (0 to 5).</param>
    /// <param name="pots">Players eligible for each pot, as indices into <paramref name="holeCards"/>.</param>
    /// <returns>For each pot, each player's expected share (0–1); a player not eligible gets 0.</returns>
    public static double[][] Compute(
        IReadOnlyList<(Card First, Card Second)> holeCards,
        IReadOnlyList<Card> board,
        IReadOnlyList<IReadOnlyCollection<int>> pots)
    {
        ArgumentNullException.ThrowIfNull(holeCards);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(pots);
        if (board.Count > 5)
        {
            throw new ArgumentException("A board has at most five cards.", nameof(board));
        }

        var players = holeCards.Count;
        var hands = new int[players * 2];
        var used = new bool[52];
        for (var p = 0; p < players; p++)
        {
            hands[p * 2] = Take(holeCards[p].First, used);
            hands[(p * 2) + 1] = Take(holeCards[p].Second, used);
        }

        var cards = new int[5];
        for (var i = 0; i < board.Count; i++)
        {
            cards[i] = Take(board[i], used);
        }

        var deck = Enumerable.Range(0, 52).Where(c => !used[c]).ToArray();
        var missing = 5 - board.Count;
        var eligible = pots.Select(pot => pot.ToArray()).ToArray();
        var wins = eligible.Select(_ => new double[players]).ToArray();
        var strength = new int[players];
        long boards = 0;

        // Walk every combination of the missing cards (indices kept increasing).
        var pick = new int[missing];
        for (var i = 0; i < missing; i++)
        {
            pick[i] = i;
        }

        while (true)
        {
            for (var i = 0; i < missing; i++)
            {
                cards[board.Count + i] = deck[pick[i]];
            }

            for (var p = 0; p < players; p++)
            {
                strength[p] = HandEvaluator.Evaluate(hands[p * 2], hands[(p * 2) + 1], cards[0], cards[1], cards[2], cards[3], cards[4]);
            }

            Award(eligible, strength, wins);
            boards++;

            if (!Next(pick, deck.Length))
            {
                break;
            }
        }

        foreach (var pot in wins)
        {
            for (var p = 0; p < players; p++)
            {
                pot[p] /= boards;
            }
        }

        return wins;
    }

    /// <summary>Each pot goes to the best eligible hand; equal best hands split it.</summary>
    private static void Award(int[][] eligible, int[] strength, double[][] wins)
    {
        for (var pot = 0; pot < eligible.Length; pot++)
        {
            var best = int.MinValue;
            var winners = 0;
            foreach (var p in eligible[pot])
            {
                if (strength[p] > best)
                {
                    best = strength[p];
                    winners = 1;
                }
                else if (strength[p] == best)
                {
                    winners++;
                }
            }

            foreach (var p in eligible[pot])
            {
                if (strength[p] == best)
                {
                    wins[pot][p] += 1.0 / winners;
                }
            }
        }
    }

    /// <summary>Next combination in lexicographic order; false after the last one (or when nothing is missing).</summary>
    private static bool Next(int[] pick, int n)
    {
        var k = pick.Length;
        var i = k - 1;
        while (i >= 0 && pick[i] == n - k + i)
        {
            i--;
        }

        if (i < 0)
        {
            return false;
        }

        pick[i]++;
        for (var j = i + 1; j < k; j++)
        {
            pick[j] = pick[j - 1] + 1;
        }

        return true;
    }

    private static int Take(Card card, bool[] used)
    {
        var index = HandEvaluator.Index(card);
        if (used[index])
        {
            throw new ArgumentException($"Card {card} appears twice.");
        }

        used[index] = true;
        return index;
    }
}
