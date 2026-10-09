namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>
/// Derives the hero's facts from one hand. Definitions (the usual tracker ones, ADR-0006):
/// <list type="bullet">
/// <item>VPIP / PFR: put money in voluntarily / raised, preflop; counted over hands with a preflop decision.</item>
/// <item>RFI (open): first to enter the pot, with a raise, when everyone before folded. Limp: same spot, a call.</item>
/// <item>Steal: an open from the cutoff, the button or the small blind.</item>
/// <item>3-bet: re-raise when facing exactly one raise. Fold to 3-bet: after opening, fold to the single re-raise.</item>
/// <item>C-bet flop: as the last preflop raiser, bet the flop when checked to (or first to act).</item>
/// <item>Went to showdown: saw the flop and was still in when two or more players remained at the end.</item>
/// <item>Postflop (fold to / raise the flop c-bet, turn c-bet, flop check-raise, won when saw flop, aggression
/// frequency): see <see cref="PostflopPlayFacts"/>.</item>
/// </list>
/// Pure and total: any well-formed hand gives facts.
/// </summary>
public static class HandAnalyzer
{
    private static readonly HashSet<PokerPosition> StealPositions =
        [PokerPosition.Cutoff, PokerPosition.Button, PokerPosition.SmallBlind];

    public static HeroHandFacts Analyze(HandForAnalysis hand)
    {
        ArgumentNullException.ThrowIfNull(hand);

        var hero = hand.Hero;
        var positions = PositionResolver.Resolve(hand);
        PokerPosition? position = positions.TryGetValue(hero, out var p) ? p : null;
        var dealt = hand.Actions.Select(a => a.Player).Distinct(StringComparer.Ordinal).Count();
        var heroStack = hand.Seats.FirstOrDefault(s => s.Player == hero)?.Stack ?? 0;

        var preflop = Preflop(hand, hero, position);
        var postflop = Postflop(hand, hero, preflop.HeroFolded, preflop.LastRaiser == hero);

        var invested = hand.Actions.Where(a => a.Player == hero).Sum(a => a.Amount);
        var net = hand.Collected.GetValueOrDefault(hero) - invested;
        var allIn = AllInExpectation.Compute(hand);
        var play = PostflopPlay.Analyze(hand, hero, preflop.LastRaiser, postflop.SawFlop, postflop.Cbet);

        return new HeroHandFacts(
            position,
            dealt,
            hand.BigBlind > 0 ? Math.Round((decimal)heroStack / hand.BigBlind, 2) : 0m,
            preflop.HadDecision,
            preflop.Vpip,
            preflop.Pfr,
            preflop.RfiOpportunity,
            preflop.Rfi,
            preflop.Limp,
            preflop.StealOpportunity,
            preflop.Steal,
            preflop.ThreeBetOpportunity,
            preflop.ThreeBet,
            preflop.FoldToThreeBetOpportunity,
            preflop.FoldToThreeBet,
            postflop.SawFlop,
            postflop.CbetOpportunity,
            postflop.Cbet,
            postflop.WentToShowdown,
            postflop.WentToShowdown && hand.Collected.GetValueOrDefault(hero) > 0,
            net,
            hand.BigBlind > 0 ? Math.Round((decimal)net / hand.BigBlind, 2) : 0m,
            allIn?.Equity,
            allIn?.ExpectedNetChips,
            play.FoldToCbetFlopOpportunity,
            play.FoldToCbetFlop,
            play.RaiseCbetFlop,
            play.CbetTurnOpportunity,
            play.CbetTurn,
            play.CheckRaiseFlopOpportunity,
            play.CheckRaiseFlop,
            play.WonWhenSawFlop,
            play.Aggressive,
            play.Decisions);
    }

    private static PreflopFacts Preflop(HandForAnalysis hand, string hero, PokerPosition? position)
    {
        var facts = new PreflopFacts();
        var raises = 0;
        var calls = 0;
        var heroOpened = false;

        foreach (var action in hand.Actions.Where(a => a.Street == Street.Preflop && !IsForced(a.Kind)))
        {
            if (action.Player == hero)
            {
                if (!facts.HadDecision)
                {
                    facts.HadDecision = true;
                    facts.RfiOpportunity = raises == 0 && calls == 0;
                    facts.ThreeBetOpportunity = raises == 1;
                    facts.StealOpportunity = facts.RfiOpportunity && position is { } pos && StealPositions.Contains(pos);
                    if (facts.RfiOpportunity)
                    {
                        facts.Rfi = action.Kind == ActionKind.Raise;
                        facts.Limp = action.Kind == ActionKind.Call;
                        facts.Steal = facts.StealOpportunity && facts.Rfi;
                        heroOpened = facts.Rfi;
                    }

                    facts.ThreeBet = facts.ThreeBetOpportunity && action.Kind == ActionKind.Raise;
                }
                else if (heroOpened && raises == 2 && !facts.FoldToThreeBetOpportunity)
                {
                    facts.FoldToThreeBetOpportunity = true;
                    facts.FoldToThreeBet = action.Kind == ActionKind.Fold;
                }

                facts.Vpip |= action.Kind is ActionKind.Call or ActionKind.Bet or ActionKind.Raise;
                facts.Pfr |= action.Kind is ActionKind.Bet or ActionKind.Raise;
                facts.HeroFolded |= action.Kind == ActionKind.Fold;
            }

            switch (action.Kind)
            {
                case ActionKind.Bet or ActionKind.Raise:
                    raises++;
                    facts.LastRaiser = action.Player;
                    break;
                case ActionKind.Call:
                    calls++;
                    break;
            }
        }

        return facts;
    }

    private static PostflopFacts Postflop(HandForAnalysis hand, string hero, bool heroFoldedPreflop, bool heroLastRaiser)
    {
        var folded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in hand.Actions.Where(a => a.Street == Street.Preflop && a.Kind == ActionKind.Fold))
        {
            folded.Add(action.Player);
        }

        var dealt = hand.Actions.Select(a => a.Player).ToHashSet(StringComparer.Ordinal);
        var sawFlop = !heroFoldedPreflop && dealt.Count(p => !folded.Contains(p)) >= 2;

        var cbetOpportunity = false;
        var cbet = false;
        if (sawFlop && heroLastRaiser)
        {
            var betBefore = false;
            foreach (var action in hand.Actions.Where(a => a.Street == Street.Flop))
            {
                if (action.Player == hero)
                {
                    cbetOpportunity = !betBefore;
                    cbet = cbetOpportunity && action.Kind == ActionKind.Bet;
                    break;
                }

                betBefore |= action.Kind is ActionKind.Bet or ActionKind.Raise;
            }
        }

        foreach (var action in hand.Actions.Where(a => a.Street != Street.Preflop && a.Kind == ActionKind.Fold))
        {
            folded.Add(action.Player);
        }

        var remaining = dealt.Count(p => !folded.Contains(p));
        var wentToShowdown = sawFlop && !folded.Contains(hero) && remaining >= 2;
        return new PostflopFacts(sawFlop, cbetOpportunity, cbet, wentToShowdown);
    }

    private static bool IsForced(ActionKind kind) =>
        kind is ActionKind.PostAnte or ActionKind.PostSmallBlind or ActionKind.PostBigBlind;

    private sealed class PreflopFacts
    {
        public bool HadDecision { get; set; }

        public bool Vpip { get; set; }

        public bool Pfr { get; set; }

        public bool RfiOpportunity { get; set; }

        public bool Rfi { get; set; }

        public bool Limp { get; set; }

        public bool StealOpportunity { get; set; }

        public bool Steal { get; set; }

        public bool ThreeBetOpportunity { get; set; }

        public bool ThreeBet { get; set; }

        public bool FoldToThreeBetOpportunity { get; set; }

        public bool FoldToThreeBet { get; set; }

        public bool HeroFolded { get; set; }

        public string? LastRaiser { get; set; }
    }

    private sealed record PostflopFacts(bool SawFlop, bool CbetOpportunity, bool Cbet, bool WentToShowdown);
}
