using PokerCoach.Domain.Poker.Evaluation;

namespace PokerCoach.Domain.Poker;

/// <summary>
/// Monte Carlo equity for ranges. Each player is represented as a list of hand-class indices
/// (0–168) matching the 13×13 grid order: row-major from aces, suited above the diagonal,
/// pairs on the diagonal, offsuit below.
/// </summary>
public static class RangeEquity
{
    // 169 hand classes → list of (card1, card2) combos using HandEvaluator card indices (0–51).
    private static readonly (int C1, int C2)[][] ClassCombos = BuildClassCombos();

    private static (int, int)[][] BuildClassCombos()
    {
        var result = new (int, int)[169][];
        for (var row = 0; row < 13; row++)
        {
            for (var col = 0; col < 13; col++)
            {
                // Grid rank indices (0 = A … 12 = 2) → HandEvaluator rank (12 = A … 0 = 2)
                var rankA = 12 - row;
                var rankB = 12 - col;
                var combos = new List<(int, int)>(12);

                if (row == col) // pair
                {
                    for (var s1 = 0; s1 < 4; s1++)
                        for (var s2 = s1 + 1; s2 < 4; s2++)
                            combos.Add((rankA * 4 + s1, rankA * 4 + s2));
                }
                else if (row < col) // suited (higher rank = row, lower rank = col)
                {
                    for (var s = 0; s < 4; s++)
                        combos.Add((rankA * 4 + s, rankB * 4 + s));
                }
                else // offsuit (higher rank = col since col < row → rankB > rankA)
                {
                    for (var s1 = 0; s1 < 4; s1++)
                        for (var s2 = 0; s2 < 4; s2++)
                            if (s1 != s2)
                                combos.Add((rankB * 4 + s1, rankA * 4 + s2));
                }

                result[row * 13 + col] = combos.ToArray();
            }
        }

        return result;
    }

    /// <param name="playerRanges">Hand-class indices (0–168) per player.</param>
    /// <param name="boardCards">Pre-dealt board cards as HandEvaluator indices (0–51); 0 to 5 cards.</param>
    /// <param name="iterations">Number of Monte Carlo samples.</param>
    /// <param name="rng">Thread-safe random source.</param>
    /// <returns>Expected equity (0–1) per player; sums to ≤ 1 (dead money if any iteration is invalid).</returns>
    public static double[] Compute(
        IReadOnlyList<IReadOnlyList<int>> playerRanges,
        IReadOnlyList<int> boardCards,
        int iterations,
        Random rng)
    {
        var n = playerRanges.Count;
        var equity = new double[n];
        var strength = new int[n];
        var hands = new int[n * 2];
        var usedCards = new bool[52];
        var board = new int[5];
        var deckBuf = new int[52];
        var boardSize = boardCards.Count;

        // Expand each player's range classes into a flat combo list.
        var allCombos = playerRanges
            .Select(r => r.SelectMany(cls => ClassCombos[cls]).ToArray())
            .ToArray();
        var availBuf = new (int, int)[1326];

        long valid = 0;

        for (var iter = 0; iter < iterations; iter++)
        {
            Array.Clear(usedCards, 0, 52);
            foreach (var c in boardCards)
                usedCards[c] = true;

            var ok = true;
            for (var p = 0; p < n && ok; p++)
            {
                var availCount = 0;
                foreach (var (c1, c2) in allCombos[p])
                    if (!usedCards[c1] && !usedCards[c2])
                        availBuf[availCount++] = (c1, c2);

                if (availCount == 0) { ok = false; break; }

                var (h1, h2) = availBuf[rng.Next(availCount)];
                hands[p * 2] = h1;
                hands[p * 2 + 1] = h2;
                usedCards[h1] = true;
                usedCards[h2] = true;
            }

            if (!ok) continue;

            // Build remaining deck.
            var deckSize = 0;
            for (var c = 0; c < 52; c++)
                if (!usedCards[c]) deckBuf[deckSize++] = c;

            // Copy known board.
            for (var i = 0; i < boardSize; i++)
                board[i] = boardCards[i];

            // Fill missing board cards via partial Fisher-Yates.
            var missing = 5 - boardSize;
            for (var i = 0; i < missing; i++)
            {
                var j = i + rng.Next(deckSize - i);
                board[boardSize + i] = deckBuf[j];
                deckBuf[j] = deckBuf[i];
            }

            // Evaluate.
            for (var p = 0; p < n; p++)
                strength[p] = HandEvaluator.Evaluate(
                    hands[p * 2], hands[p * 2 + 1],
                    board[0], board[1], board[2], board[3], board[4]);

            // Award.
            var best = int.MinValue;
            var winners = 0;
            for (var p = 0; p < n; p++)
            {
                if (strength[p] > best) { best = strength[p]; winners = 1; }
                else if (strength[p] == best) winners++;
            }

            for (var p = 0; p < n; p++)
                if (strength[p] == best) equity[p] += 1.0 / winners;

            valid++;
        }

        if (valid > 0)
            for (var p = 0; p < n; p++) equity[p] /= valid;

        return equity;
    }
}
