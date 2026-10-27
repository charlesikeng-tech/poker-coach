namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>
/// One opponent's raw facts from a single hand: additive counters that accumulate across hands.
/// Computed by <see cref="OpponentFactsExtractor"/>; stored and aggregated by infrastructure.
/// </summary>
public sealed record OpponentHandFacts(
    bool HadDecision,
    bool Vpip,
    bool Pfr,
    bool ThreeBetOpportunity,
    bool ThreeBet,
    bool SawFlop,
    bool WentToShowdown,
    int PostflopAggressive,
    int PostflopDecisions);

/// <summary>
/// Extracts per-opponent preflop and postflop facts from one hand. Same definitions as
/// <see cref="HandAnalyzer"/> (VPIP, PFR, 3-bet, AF, WTSD) but applied to every non-hero player.
/// </summary>
public static class OpponentFactsExtractor
{
    public static IReadOnlyDictionary<string, OpponentHandFacts> Extract(HandForAnalysis hand)
    {
        ArgumentNullException.ThrowIfNull(hand);

        var dealt = hand.Actions.Select(a => a.Player).ToHashSet(StringComparer.Ordinal);
        var opponents = dealt.Where(p => p != hand.Hero).ToList();
        if (opponents.Count == 0) return new Dictionary<string, OpponentHandFacts>();

        var preflopActions = hand.Actions.Where(a => a.Street == Street.Preflop && !IsForced(a.Kind)).ToList();

        var hadDecision = new HashSet<string>(StringComparer.Ordinal);
        var vpip = new HashSet<string>(StringComparer.Ordinal);
        var pfr = new HashSet<string>(StringComparer.Ordinal);
        var threeBetOpp = new HashSet<string>(StringComparer.Ordinal);
        var threeBet = new HashSet<string>(StringComparer.Ordinal);

        var raises = 0;
        var firstDecisionSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var action in preflopActions)
        {
            if (action.Player == hand.Hero)
            {
                if (action.Kind is ActionKind.Bet or ActionKind.Raise) raises++;
                continue;
            }

            if (!firstDecisionSeen.Contains(action.Player))
            {
                firstDecisionSeen.Add(action.Player);
                hadDecision.Add(action.Player);

                if (raises == 1)
                {
                    threeBetOpp.Add(action.Player);
                    if (action.Kind is ActionKind.Raise)
                        threeBet.Add(action.Player);
                }
            }

            if (action.Kind is ActionKind.Call or ActionKind.Bet or ActionKind.Raise)
                vpip.Add(action.Player);
            if (action.Kind is ActionKind.Bet or ActionKind.Raise)
                pfr.Add(action.Player);

            if (action.Kind is ActionKind.Bet or ActionKind.Raise) raises++;
        }

        var foldedPreflop = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in hand.Actions.Where(a => a.Street == Street.Preflop && a.Kind == ActionKind.Fold))
            foldedPreflop.Add(action.Player);

        var sawFlop = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in opponents)
        {
            if (!foldedPreflop.Contains(p) && dealt.Count(d => !foldedPreflop.Contains(d)) >= 2)
                sawFlop.Add(p);
        }

        var foldedPostflop = new HashSet<string>(foldedPreflop, StringComparer.Ordinal);
        var aggressive = new Dictionary<string, int>(StringComparer.Ordinal);
        var decisions = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var action in hand.Actions.Where(a => a.Street != Street.Preflop))
        {
            if (action.Player == hand.Hero) continue;

            if (action.Kind == ActionKind.Fold)
            {
                foldedPostflop.Add(action.Player);
                decisions[action.Player] = decisions.GetValueOrDefault(action.Player) + 1;
                continue;
            }

            if (action.Kind is ActionKind.Call)
                decisions[action.Player] = decisions.GetValueOrDefault(action.Player) + 1;
            else if (action.Kind is ActionKind.Bet or ActionKind.Raise)
            {
                aggressive[action.Player] = aggressive.GetValueOrDefault(action.Player) + 1;
                decisions[action.Player] = decisions.GetValueOrDefault(action.Player) + 1;
            }
            else if (action.Kind is ActionKind.Check)
            {
                decisions[action.Player] = decisions.GetValueOrDefault(action.Player) + 1;
            }
        }

        var remaining = dealt.Count(p => !foldedPostflop.Contains(p));
        var wentToShowdown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in opponents)
        {
            if (sawFlop.Contains(p) && !foldedPostflop.Contains(p) && remaining >= 2)
                wentToShowdown.Add(p);
        }

        var result = new Dictionary<string, OpponentHandFacts>(StringComparer.Ordinal);
        foreach (var p in opponents)
        {
            if (!hadDecision.Contains(p)) continue;

            result[p] = new OpponentHandFacts(
                true,
                vpip.Contains(p),
                pfr.Contains(p),
                threeBetOpp.Contains(p),
                threeBet.Contains(p),
                sawFlop.Contains(p),
                wentToShowdown.Contains(p),
                aggressive.GetValueOrDefault(p),
                decisions.GetValueOrDefault(p));
        }

        return result;
    }

    private static bool IsForced(ActionKind kind) =>
        kind is ActionKind.PostAnte or ActionKind.PostSmallBlind or ActionKind.PostBigBlind;
}
