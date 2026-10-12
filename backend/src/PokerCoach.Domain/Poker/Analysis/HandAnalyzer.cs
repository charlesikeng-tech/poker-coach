namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>
/// Derives the hero's facts from one hand. Definitions (the usual tracker ones, ADR-0006):
/// <list type="bullet">
/// <item>VPIP / PFR: put money in voluntarily / raised, preflop; counted over hands with a preflop decision.</item>
/// <item>RFI (open): first to enter the pot, with a raise, when everyone before folded. Limp: same spot, a call.</item>
/// <item>Steal: an open from the cutoff, the button or the small blind.</item>
/// <item>3-bet: re-raise when facing exactly one raise. Fold to 3-bet: after opening, fold to the single re-raise.</item>
/// <item>Facing an open: the first decision faces one raise that is not all-in, nobody having limped or called
/// before; the hero calls, 3-bets or folds. A narrower spot than the 3-bet opportunity (no squeeze, no
/// open-shove), the one defence ranges are written for.</item>
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

        var preflop = Preflop(hand, hero, position, positions);
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
            play.Decisions,
            preflop.FacingOpenFrom,
            preflop.FacingOpenCall,
            preflop.FacingOpenThreeBet,
            InBigBlinds(preflop.OpenTo, hand.BigBlind),
            preflop.OpenAllIn,
            InBigBlinds(preflop.FacingOpenTo, hand.BigBlind),
            InBigBlinds(preflop.FacingOpenEffectiveStack, hand.BigBlind),
            InBigBlinds(preflop.ThreeBetTo, hand.BigBlind),
            preflop.ThreeBetAllIn);
    }

    private static PreflopFacts Preflop(HandForAnalysis hand, string hero, PokerPosition? position, IReadOnlyDictionary<string, PokerPosition> positions)
    {
        var facts = new PreflopFacts();
        // Chips each player has in front of him preflop, blinds included and antes not: a raise's "to" amount.
        var putIn = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var blind in hand.Actions.Where(a => a.Street == Street.Preflop && a.Kind is ActionKind.PostSmallBlind or ActionKind.PostBigBlind))
        {
            putIn[blind.Player] = putIn.GetValueOrDefault(blind.Player) + blind.Amount;
        }

        var raises = 0;
        var calls = 0;
        var heroOpened = false;
        var lastRaiseAllIn = false;

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
                        if (facts.Rfi)
                        {
                            facts.OpenTo = putIn.GetValueOrDefault(hero) + action.Amount;
                            facts.OpenAllIn = action.IsAllIn;
                        }
                    }

                    facts.ThreeBet = facts.ThreeBetOpportunity && action.Kind == ActionKind.Raise;

                    if (raises == 1 && calls == 0 && !lastRaiseAllIn
                        && facts.LastRaiser is { } opener && positions.TryGetValue(opener, out var openerPosition))
                    {
                        facts.FacingOpenFrom = openerPosition;
                        facts.FacingOpenCall = action.Kind == ActionKind.Call;
                        facts.FacingOpenThreeBet = action.Kind == ActionKind.Raise;
                        facts.FacingOpenTo = putIn.GetValueOrDefault(opener);
                        facts.FacingOpenEffectiveStack = Math.Min(StackOf(hand, hero), StackOf(hand, opener));
                        if (facts.FacingOpenThreeBet)
                        {
                            facts.ThreeBetTo = putIn.GetValueOrDefault(hero) + action.Amount;
                            facts.ThreeBetAllIn = action.IsAllIn;
                        }
                    }
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

            putIn[action.Player] = putIn.GetValueOrDefault(action.Player) + action.Amount;
            switch (action.Kind)
            {
                case ActionKind.Bet or ActionKind.Raise:
                    raises++;
                    facts.LastRaiser = action.Player;
                    lastRaiseAllIn = action.IsAllIn;
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

    private static long StackOf(HandForAnalysis hand, string player) =>
        hand.Seats.FirstOrDefault(s => s.Player == player)?.Stack ?? 0;

    private static decimal? InBigBlinds(long? chips, long bigBlind) =>
        chips is { } c && bigBlind > 0 ? Math.Round((decimal)c / bigBlind, 2) : null;

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

        public PokerPosition? FacingOpenFrom { get; set; }

        public bool FacingOpenCall { get; set; }

        public bool FacingOpenThreeBet { get; set; }

        public long? OpenTo { get; set; }

        public bool OpenAllIn { get; set; }

        public long? FacingOpenTo { get; set; }

        public long? FacingOpenEffectiveStack { get; set; }

        public long? ThreeBetTo { get; set; }

        public bool ThreeBetAllIn { get; set; }
    }

    private sealed record PostflopFacts(bool SawFlop, bool CbetOpportunity, bool Cbet, bool WentToShowdown);
}
