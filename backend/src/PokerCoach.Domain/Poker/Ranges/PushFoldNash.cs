using System.Collections.Concurrent;
using PokerCoach.Domain.Poker.Analysis;

namespace PokerCoach.Domain.Poker.Ranges;

/// <summary>Shove and call ranges of a push/fold equilibrium for one table format and stack.</summary>
public sealed class PushFoldChart(
    TableFormat format,
    int stackBigBlinds,
    IReadOnlyDictionary<PokerPosition, IReadOnlySet<HandClass>> shoves,
    IReadOnlyDictionary<(PokerPosition Caller, PokerPosition Shover), IReadOnlySet<HandClass>> calls)
{
    public TableFormat Format { get; } = format;

    public int StackBigBlinds { get; } = stackBigBlinds;

    /// <summary>What a seat shoves when folded to (empty for the big blind).</summary>
    public IReadOnlySet<HandClass> Shove(PokerPosition position) =>
        shoves.TryGetValue(position, out var range) ? range : new HashSet<HandClass>();

    /// <summary>What a seat calls a shove from <paramref name="shover"/> with, the players between having folded.</summary>
    public IReadOnlySet<HandClass> Call(PokerPosition caller, PokerPosition shover) =>
        calls.TryGetValue((caller, shover), out var range) ? range : new HashSet<HandClass>();
}

/// <summary>
/// Push/fold equilibrium in chip EV (ADR-0009, block 4): when folded to, a seat either shoves its stack or
/// folds; each player behind calls or folds. Solved by fictitious play over the 169 starting hands, with
/// card removal on the shover's hand.
/// <para>
/// Assumptions, stated in the product: every player has the same stack; antes of 0.125 BB per player plus
/// 0.5 / 1 BB blinds; a caller assumes the players after him fold (multi-way all-ins are left out, as in
/// the usual push/fold charts). Heads-up (small blind against big blind) it is the exact equilibrium.
/// </para>
/// </summary>
public static class PushFoldNash
{
    public const decimal Ante = 0.125m;
    public const int MinStack = 3;
    public const int MaxStack = 15;
    public const int Iterations = 250;

    private static readonly ConcurrentDictionary<(TableFormat, int, decimal), Lazy<PushFoldChart>> Cache = new();

    /// <summary>Seats in preflop action order for a format.</summary>
    public static IReadOnlyList<PokerPosition> Seats(TableFormat format) => format == TableFormat.SixMax
        ? [PokerPosition.Utg, PokerPosition.Hijack, PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind, PokerPosition.BigBlind]
        : [PokerPosition.Utg, PokerPosition.Utg1, PokerPosition.Utg2, PokerPosition.Lojack, PokerPosition.Hijack, PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind, PokerPosition.BigBlind];

    /// <summary>The chart for a stack (in big blinds, clamped to 3–15), computed once then cached.</summary>
    public static PushFoldChart Solve(TableFormat format, int stackBigBlinds, decimal ante = Ante)
    {
        var stack = Math.Clamp(stackBigBlinds, MinStack, MaxStack);
        return Cache.GetOrAdd((format, stack, ante), key => new Lazy<PushFoldChart>(() => Compute(key.Item1, key.Item2, (double)key.Item3))).Value;
    }

    private static PushFoldChart Compute(TableFormat format, int stack, double ante)
    {
        const int n = PreflopEquity.Hands;
        var seats = Seats(format);
        var players = seats.Count;
        double Blind(PokerPosition p) => p == PokerPosition.SmallBlind ? 0.5 : p == PokerPosition.BigBlind ? 1 : 0;
        double stackBb = stack;

        // Pot when `shover` and `caller` are all in and the others folded: both stacks, every other ante,
        // and the blinds of players outside the pot.
        double Pot(PokerPosition shover, PokerPosition caller) =>
            (2 * stackBb) + ((players - 2) * ante) + seats.Where(p => p != shover && p != caller).Sum(Blind);

        var shovers = seats.Take(players - 1).ToList();
        var push = shovers.ToDictionary(p => p, _ => Enumerable.Repeat(1d, n).ToArray());
        var call = new Dictionary<(PokerPosition, PokerPosition), double[]>();
        for (var s = 0; s < shovers.Count; s++)
        {
            foreach (var caller in seats.Skip(s + 1))
            {
                call[(caller, shovers[s])] = new double[n];
            }
        }

        var best = new double[n];
        for (var t = 1; t <= Iterations; t++)
        {
            // Callers: call when the equity against the shoving range wins more than folding keeps.
            foreach (var ((caller, shover), frequency) in call)
            {
                var range = push[shover];
                var pot = Pot(shover, caller);
                var keep = stackBb - ante - Blind(caller);
                for (var x = 0; x < n; x++)
                {
                    double weight = 0, equity = 0;
                    for (var h = 0; h < n; h++)
                    {
                        var w = PreflopEquity.Weight(x, h) * range[h];
                        weight += w;
                        equity += w * PreflopEquity.Of(x, h);
                    }

                    best[x] = weight > 0 && equity / weight * pot > keep ? 1 : 0;
                }

                Average(frequency, best, t);
            }

            // Shovers: chips won against folding, over who calls first behind.
            for (var s = 0; s < shovers.Count; s++)
            {
                var shover = shovers[s];
                var keep = stackBb - ante - Blind(shover);
                var everyoneFolds = (players * ante) + 1.5;
                var gain = new double[n];
                var noCall = Enumerable.Repeat(1d, n).ToArray();
                foreach (var caller in seats.Skip(s + 1))
                {
                    var range = call[(caller, shover)];
                    var pot = Pot(shover, caller);
                    for (var h = 0; h < n; h++)
                    {
                        double all = 0, calling = 0, equity = 0;
                        for (var x = 0; x < n; x++)
                        {
                            var w = PreflopEquity.Weight(h, x);
                            all += w;
                            calling += w * range[x];
                            equity += w * range[x] * PreflopEquity.Of(h, x);
                        }

                        var called = calling / all;
                        if (calling > 0)
                        {
                            gain[h] += noCall[h] * called * ((equity / calling * pot) - keep);
                        }

                        noCall[h] *= 1 - called;
                    }
                }

                for (var h = 0; h < n; h++)
                {
                    best[h] = gain[h] + (noCall[h] * everyoneFolds) > 0 ? 1 : 0;
                }

                Average(push[shover], best, t);
            }
        }

        static IReadOnlySet<HandClass> Pure(double[] frequency) =>
            HandClass.All.Where((_, i) => frequency[i] >= 0.5).ToHashSet();

        return new PushFoldChart(
            format,
            stack,
            push.ToDictionary(e => e.Key, e => Pure(e.Value)),
            call.ToDictionary(e => e.Key, e => Pure(e.Value)));
    }

    /// <summary>Fictitious play: each strategy is the running average of best responses.</summary>
    private static void Average(double[] frequency, double[] best, int round)
    {
        for (var i = 0; i < frequency.Length; i++)
        {
            frequency[i] += (best[i] - frequency[i]) / round;
        }
    }
}
