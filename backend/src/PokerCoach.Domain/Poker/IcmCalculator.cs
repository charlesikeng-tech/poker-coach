using System.Numerics;

namespace PokerCoach.Domain.Poker;

/// <summary>
/// Malmuth-Harville ICM: converts chip stacks to monetary equity given a payout structure.
/// Exact for ≤10 players (O(2ⁿ × n²) with bitmask memoization).
/// </summary>
public static class IcmCalculator
{
    public const int MaxPlayers = 10;

    /// <param name="stacks">Chip count per player (same unit, any positive value).</param>
    /// <param name="payouts">Prize per finishing position (index 0 = 1st). May be shorter than stacks.</param>
    /// <returns>Monetary equity per player, in the same unit as payouts.</returns>
    public static decimal[] Calculate(ReadOnlySpan<decimal> stacks, ReadOnlySpan<decimal> payouts)
    {
        var n = stacks.Length;
        if (n == 0) return [];
        ArgumentOutOfRangeException.ThrowIfGreaterThan(n, MaxPlayers, nameof(stacks));

        var k = Math.Min(payouts.Length, n);
        if (k == 0) return new decimal[n];

        var s = stacks.ToArray();
        var p = payouts[..k].ToArray();
        var memo = new decimal[1 << n][];

        ComputeEquity((1 << n) - 1);

        return (decimal[])memo[(1 << n) - 1]!.Clone();

        decimal[] ComputeEquity(int mask)
        {
            if (memo[mask] is { } cached) return cached;

            var prizeIndex = n - BitOperations.PopCount((uint)mask);
            var equity = new decimal[n];

            if (prizeIndex < k)
            {
                decimal total = 0;
                for (var j = 0; j < n; j++)
                    if ((mask & (1 << j)) != 0) total += s[j];

                for (var j = 0; j < n; j++)
                {
                    if ((mask & (1 << j)) == 0) continue;
                    var prob = s[j] / total;
                    equity[j] += prob * p[prizeIndex];

                    var remaining = mask ^ (1 << j);
                    if (remaining != 0 && prizeIndex + 1 < k)
                    {
                        var sub = ComputeEquity(remaining);
                        for (var i = 0; i < n; i++)
                            if (i != j && (mask & (1 << i)) != 0)
                                equity[i] += prob * sub[i];
                    }
                }
            }

            memo[mask] = equity;
            return equity;
        }
    }
}
