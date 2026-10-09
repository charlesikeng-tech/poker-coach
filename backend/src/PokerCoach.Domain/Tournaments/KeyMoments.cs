namespace PokerCoach.Domain.Tournaments;

/// <summary>Where the hero's hand was decided, from the facts we measured (no interpretation).</summary>
public enum KeyMomentStage
{
    /// <summary>The hand ended before the flop for the hero (won or lost preflop).</summary>
    Preflop,

    /// <summary>The hero saw the flop and the hand ended before showdown.</summary>
    Postflop,

    Showdown,
}

/// <summary>One hand of a tournament, reduced to what key-moment selection needs.</summary>
/// <param name="Index">Position of the hand in the tournament, from 1, in play order.</param>
/// <param name="HeroStack">Hero's chips at the start of the hand.</param>
/// <param name="NetChips">Chips won minus chips put in during the hand.</param>
public sealed record KeyMomentCandidate(
    int Index,
    long HeroStack,
    long NetChips,
    decimal NetBigBlinds,
    bool SawFlop,
    bool WentToShowdown);

/// <param name="StackShare">Net chips over the starting stack: +1 is a double-up, −1 a bust.</param>
public sealed record KeyMoment(int Index, long NetChips, decimal NetBigBlinds, decimal StackShare, KeyMomentStage Stage);

/// <summary>
/// The hands that moved the hero's tournament the most. Ranked by share of the stack won or lost, not by
/// big blinds: a double-up at 15 BB decides a tournament, a 20 BB pot at 150 BB does not.
/// </summary>
public static class KeyMoments
{
    public const int MaxCount = 8;

    /// <summary>Below a quarter of the stack, a hand did not change the course of the tournament.</summary>
    public const decimal MinStackShare = 0.25m;

    /// <returns>At most <see cref="MaxCount"/> moments, in play order.</returns>
    public static IReadOnlyList<KeyMoment> Select(IEnumerable<KeyMomentCandidate> hands)
    {
        ArgumentNullException.ThrowIfNull(hands);

        return hands
            .Where(h => h.HeroStack > 0 && h.NetChips != 0)
            .Select(h => new KeyMoment(
                h.Index,
                h.NetChips,
                h.NetBigBlinds,
                Math.Round((decimal)h.NetChips / h.HeroStack, 4),
                h.WentToShowdown ? KeyMomentStage.Showdown : h.SawFlop ? KeyMomentStage.Postflop : KeyMomentStage.Preflop))
            .Where(m => Math.Abs(m.StackShare) >= MinStackShare)
            .OrderByDescending(m => Math.Abs(m.StackShare))
            .ThenBy(m => m.Index)
            .Take(MaxCount)
            .OrderBy(m => m.Index)
            .ToList();
    }
}
