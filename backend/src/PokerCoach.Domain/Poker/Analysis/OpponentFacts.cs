namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>
/// One opponent's raw facts from a single hand: additive counters that accumulate across hands.
/// Computed by <see cref="OpponentFactsExtractor"/>; stored and aggregated by infrastructure.
/// </summary>
public sealed record OpponentHandFacts(
    bool HadDecision,
    bool Vpip,
    bool Pfr,
    bool RfiOpportunity,
    bool Rfi,
    bool StealOpportunity,
    bool Steal,
    bool ThreeBetOpportunity,
    bool ThreeBet,
    bool FoldToThreeBetOpportunity,
    bool FoldToThreeBet,
    bool SawFlop,
    bool CbetFlopOpportunity,
    bool CbetFlop,
    bool FoldToCbetFlopOpportunity,
    bool FoldToCbetFlop,
    bool WentToShowdown,
    bool WonAtShowdown,
    int PostflopAggressive,
    int PostflopDecisions);

/// <summary>
/// Extracts per-opponent preflop and postflop facts from one hand. Same definitions as
/// <see cref="HandAnalyzer"/> (VPIP, PFR, 3-bet, AF, WTSD) but applied to every non-hero player.
/// </summary>
public static class OpponentFactsExtractor
{
    private static readonly HashSet<PokerPosition> StealPositions =
        [PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind];

    public static IReadOnlyDictionary<string, OpponentHandFacts> Extract(HandForAnalysis hand)
    {
        ArgumentNullException.ThrowIfNull(hand);

        var dealt = hand.Actions.Select(a => a.Player).ToHashSet(StringComparer.Ordinal);
        var opponents = dealt.Where(p => p != hand.Hero).ToList();
        if (opponents.Count == 0) return new Dictionary<string, OpponentHandFacts>();

        var positions = PositionResolver.Resolve(hand);
        var preflopActions = hand.Actions.Where(a => a.Street == Street.Preflop && !IsForced(a.Kind)).ToList();

        var state = new Dictionary<string, PreflopState>(StringComparer.Ordinal);

        var raises = 0;
        var calls = 0;
        string? lastRaiser = null;

        foreach (var action in preflopActions)
        {
            if (action.Player == hand.Hero)
            {
                if (action.Kind is ActionKind.Bet or ActionKind.Raise)
                {
                    raises++;
                    lastRaiser = action.Player;
                }
                else if (action.Kind is ActionKind.Call)
                {
                    calls++;
                }

                continue;
            }

            if (!state.TryGetValue(action.Player, out var ps))
            {
                ps = new PreflopState();
                state[action.Player] = ps;
            }

            if (!ps.HadDecision)
            {
                ps.HadDecision = true;
                ps.RfiOpportunity = raises == 0 && calls == 0;
                ps.ThreeBetOpportunity = raises == 1;

                if (ps.RfiOpportunity)
                {
                    ps.Rfi = action.Kind is ActionKind.Raise;
                    if (ps.Rfi && positions.TryGetValue(action.Player, out var pos))
                        ps.StealOpportunity = StealPositions.Contains(pos);
                    ps.Steal = ps.StealOpportunity && ps.Rfi;
                }

                if (ps.ThreeBetOpportunity)
                    ps.ThreeBet = action.Kind is ActionKind.Raise;
            }
            else if (ps.Opened && raises == 2 && !ps.FoldToThreeBetOpportunity)
            {
                ps.FoldToThreeBetOpportunity = true;
                ps.FoldToThreeBet = action.Kind == ActionKind.Fold;
            }

            ps.Vpip |= action.Kind is ActionKind.Call or ActionKind.Bet or ActionKind.Raise;
            ps.Pfr |= action.Kind is ActionKind.Bet or ActionKind.Raise;
            ps.Folded |= action.Kind == ActionKind.Fold;

            if (action.Kind is ActionKind.Bet or ActionKind.Raise)
            {
                raises++;
                lastRaiser = action.Player;
            }
            else if (action.Kind is ActionKind.Call)
            {
                calls++;
            }
        }

        var foldedPreflop = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in hand.Actions.Where(a => a.Street == Street.Preflop && a.Kind == ActionKind.Fold))
            foldedPreflop.Add(action.Player);

        var playersAtFlop = dealt.Count(d => !foldedPreflop.Contains(d));
        var sawFlop = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in opponents)
        {
            if (!foldedPreflop.Contains(p) && playersAtFlop >= 2)
                sawFlop.Add(p);
        }

        var cbetOpp = new Dictionary<string, bool>(StringComparer.Ordinal);
        var cbetDone = new Dictionary<string, bool>(StringComparer.Ordinal);
        var foldToCbetOpp = new Dictionary<string, bool>(StringComparer.Ordinal);
        var foldToCbet = new Dictionary<string, bool>(StringComparer.Ordinal);

        if (lastRaiser is not null && sawFlop.Count > 0)
        {
            var flopActions = hand.Actions.Where(a => a.Street == Street.Flop).ToList();
            var betBefore = false;
            string? cbettor = null;

            foreach (var action in flopActions)
            {
                if (action.Player == lastRaiser && !betBefore)
                {
                    if (state.ContainsKey(lastRaiser))
                    {
                        cbetOpp[lastRaiser] = true;
                        cbetDone[lastRaiser] = action.Kind == ActionKind.Bet;
                        if (cbetDone[lastRaiser]) cbettor = lastRaiser;
                    }

                    break;
                }

                if (action.Player != lastRaiser && action.Player != hand.Hero)
                {
                    if (action.Kind is ActionKind.Bet or ActionKind.Raise)
                        betBefore = true;
                }
            }

            if (cbettor is not null)
            {
                foreach (var action in flopActions)
                {
                    if (action.Player == cbettor) continue;
                    if (action.Player == hand.Hero) continue;
                    if (!sawFlop.Contains(action.Player)) continue;
                    if (action.Kind == ActionKind.Check) continue;

                    foldToCbetOpp[action.Player] = true;
                    foldToCbet[action.Player] = action.Kind == ActionKind.Fold;
                    break;
                }
            }
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
            if (!state.TryGetValue(p, out var ps) || !ps.HadDecision) continue;

            result[p] = new OpponentHandFacts(
                true,
                ps.Vpip,
                ps.Pfr,
                ps.RfiOpportunity,
                ps.Rfi,
                ps.StealOpportunity,
                ps.Steal,
                ps.ThreeBetOpportunity,
                ps.ThreeBet,
                ps.FoldToThreeBetOpportunity,
                ps.FoldToThreeBet,
                sawFlop.Contains(p),
                cbetOpp.GetValueOrDefault(p),
                cbetDone.GetValueOrDefault(p),
                foldToCbetOpp.GetValueOrDefault(p),
                foldToCbet.GetValueOrDefault(p),
                wentToShowdown.Contains(p),
                wentToShowdown.Contains(p) && hand.Collected.GetValueOrDefault(p) > 0,
                aggressive.GetValueOrDefault(p),
                decisions.GetValueOrDefault(p));
        }

        return result;
    }

    private static bool IsForced(ActionKind kind) =>
        kind is ActionKind.PostAnte or ActionKind.PostSmallBlind or ActionKind.PostBigBlind;

    private sealed class PreflopState
    {
        public bool HadDecision { get; set; }
        public bool Vpip { get; set; }
        public bool Pfr { get; set; }
        public bool RfiOpportunity { get; set; }
        public bool Rfi { get; set; }
        public bool StealOpportunity { get; set; }
        public bool Steal { get; set; }
        public bool ThreeBetOpportunity { get; set; }
        public bool ThreeBet { get; set; }
        public bool FoldToThreeBetOpportunity { get; set; }
        public bool FoldToThreeBet { get; set; }
        public bool Folded { get; set; }
        public bool Opened => Rfi;
    }
}
