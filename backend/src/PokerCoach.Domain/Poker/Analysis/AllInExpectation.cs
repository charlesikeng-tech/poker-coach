using PokerCoach.Domain.Poker.Evaluation;

namespace PokerCoach.Domain.Poker.Analysis;

/// <param name="Equity">The hero's share of the main pot, the one every all-in player contests (0–1).</param>
/// <param name="ExpectedNetChips">What the hero would win on average from this point, minus what he put in.</param>
public sealed record AllInOutcome(decimal Equity, decimal ExpectedNetChips);

/// <summary>
/// "All-in EV" (chip EV): when the money goes in before the flop and the cards are seen, what the hero
/// should have won given everyone's cards, against what the board gave him. Over many all-ins the gap
/// between the two is luck; the expected figure is what the play earned.
/// <para>
/// A hand counts when: the hero did not fold, at least one player was all-in preflop, no one acted after
/// the flop (all the betting was over), and every player still in showed his cards. Anything else is
/// left out rather than guessed: an all-in without both hands shown has no knowable equity.
/// </para>
/// </summary>
public static class AllInExpectation
{
    /// <returns>Null when the hand is not a preflop all-in with every hand known.</returns>
    public static AllInOutcome? Compute(HandForAnalysis hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        if (hand.KnownCards is not { } known)
        {
            return null;
        }

        var preflop = hand.Actions.Where(a => a.Street == Street.Preflop).ToList();
        if (!preflop.Any(a => a.IsAllIn) || hand.Actions.Any(a => a.Street != Street.Preflop))
        {
            return null;
        }

        var folded = preflop.Where(a => a.Kind == ActionKind.Fold).Select(a => a.Player).ToHashSet(StringComparer.Ordinal);
        var contributions = hand.Actions
            .GroupBy(a => a.Player, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount), StringComparer.Ordinal);
        var live = contributions.Keys.Where(p => !folded.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (folded.Contains(hand.Hero) || !live.Contains(hand.Hero) || live.Count < 2 || live.Any(p => !known.ContainsKey(p)))
        {
            return null;
        }

        var (pots, returned) = BuildPots(contributions, live);
        var total = pots.Sum(p => p.Amount) + returned.Values.Sum();
        if (total != hand.Collected.Values.Sum())
        {
            // The room's numbers do not add up the way we read them: better no figure than a wrong one.
            return null;
        }

        var heroIndex = live.IndexOf(hand.Hero);
        var shares = ShowdownEquity.Compute(
            live.Select(p => known[p]).ToList(),
            [],
            pots.Select(p => (IReadOnlyCollection<int>)p.Eligible.Select(player => live.IndexOf(player)).ToList()).ToList());

        var expected = returned.GetValueOrDefault(hand.Hero) + pots.Select((pot, i) => pot.Amount * shares[i][heroIndex]).Sum();
        return new AllInOutcome(
            Math.Round((decimal)shares[0][heroIndex], 4),
            Math.Round((decimal)expected - contributions[hand.Hero], 2));
    }

    internal sealed record Pot(long Amount, IReadOnlyList<string> Eligible);

    /// <summary>
    /// Main pot and side pots from what everyone put in. The part of the biggest bet nobody matched goes
    /// back to its owner; a folded player's chips stay in the pots he paid into.
    /// </summary>
    internal static (List<Pot> Pots, Dictionary<string, long> Returned) BuildPots(
        IReadOnlyDictionary<string, long> contributions,
        IReadOnlyList<string> live)
    {
        var ordered = contributions.OrderByDescending(c => c.Value).ToList();
        var returned = new Dictionary<string, long>(StringComparer.Ordinal);
        var capped = contributions.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        if (ordered.Count >= 2 && ordered[0].Value > ordered[1].Value)
        {
            returned[ordered[0].Key] = ordered[0].Value - ordered[1].Value;
            capped[ordered[0].Key] = ordered[1].Value;
        }

        var levels = live.Select(p => capped[p]).Where(v => v > 0).Distinct().Order().ToList();
        var pots = new List<Pot>();
        long previous = 0;
        foreach (var level in levels)
        {
            var amount = capped.Values.Sum(v => Math.Clamp(v, previous, level) - previous);
            var eligible = live.Where(p => capped[p] >= level).ToList();
            pots.Add(new Pot(amount, eligible));
            previous = level;
        }

        // Folded money above the last live level (a raise folded to a smaller all-in) goes to the last pot.
        var dead = capped.Values.Sum(v => Math.Max(0, v - previous));
        if (dead > 0 && pots.Count > 0)
        {
            pots[^1] = pots[^1] with { Amount = pots[^1].Amount + dead };
        }

        return (pots, returned);
    }
}
