namespace PokerCoach.Domain.Poker.Analysis;

/// <summary>
/// The hero's postflop play in one hand, beyond the flop c-bet (ADR-0006, facts version 3). Each "made"
/// flag has its opportunity, as for the preflop statistics.
/// </summary>
/// <param name="FoldToCbetFlopOpportunity">The preflop raiser (not the hero) bet the flop first and the hero
/// had to answer that bet, nobody having raised it before him.</param>
/// <param name="FoldToCbetFlop">He folded to it.</param>
/// <param name="RaiseCbetFlop">He raised it.</param>
/// <param name="CbetTurnOpportunity">The hero c-bet the flop, was only called, and the turn was checked to him
/// (or he acted first).</param>
/// <param name="CbetTurn">He bet the turn again.</param>
/// <param name="CheckRaiseFlopOpportunity">The hero checked the flop and then faced a bet.</param>
/// <param name="CheckRaiseFlop">He raised it.</param>
/// <param name="WonWhenSawFlop">Saw the flop and collected chips.</param>
/// <param name="Aggressive">Bets and raises after the flop.</param>
/// <param name="Decisions">Bets, raises, calls and folds after the flop (checks left out: the usual
/// aggression frequency).</param>
internal sealed record PostflopPlayFacts(
    bool FoldToCbetFlopOpportunity,
    bool FoldToCbetFlop,
    bool RaiseCbetFlop,
    bool CbetTurnOpportunity,
    bool CbetTurn,
    bool CheckRaiseFlopOpportunity,
    bool CheckRaiseFlop,
    bool WonWhenSawFlop,
    int Aggressive,
    int Decisions);

internal static class PostflopPlay
{
    public static PostflopPlayFacts Analyze(HandForAnalysis hand, string hero, string? preflopRaiser, bool sawFlop, bool heroCbetFlop)
    {
        if (!sawFlop)
        {
            return new PostflopPlayFacts(false, false, false, false, false, false, false, false, 0, 0);
        }

        var flop = hand.Actions.Where(a => a.Street == Street.Flop).ToList();
        var turn = hand.Actions.Where(a => a.Street == Street.Turn).ToList();

        var (facingCbet, foldToCbet, raiseCbet) = AgainstCbet(flop, hero, preflopRaiser);
        var (checkRaiseOpportunity, checkRaise) = CheckRaise(flop, hero);
        var (turnOpportunity, turnCbet) = SecondBarrel(flop, turn, hero, heroCbetFlop);

        var postflop = hand.Actions.Where(a => a.Street != Street.Preflop && a.Player == hero).ToList();
        var aggressive = postflop.Count(a => a.Kind is ActionKind.Bet or ActionKind.Raise);
        var decisions = postflop.Count(a => a.Kind is ActionKind.Bet or ActionKind.Raise or ActionKind.Call or ActionKind.Fold);

        return new PostflopPlayFacts(
            facingCbet,
            foldToCbet,
            raiseCbet,
            turnOpportunity,
            turnCbet,
            checkRaiseOpportunity,
            checkRaise,
            hand.Collected.GetValueOrDefault(hero) > 0,
            aggressive,
            decisions);
    }

    private static (bool Opportunity, bool Fold, bool Raise) AgainstCbet(List<HandAction> flop, string hero, string? preflopRaiser)
    {
        if (preflopRaiser is null || preflopRaiser == hero)
        {
            return (false, false, false);
        }

        var betSeen = false;
        var cbet = false;
        foreach (var action in flop)
        {
            if (action.Player == hero)
            {
                if (cbet)
                {
                    return (true, action.Kind == ActionKind.Fold, action.Kind == ActionKind.Raise);
                }

                if (betSeen)
                {
                    // Someone else bet first: not a c-bet the hero faces.
                    return (false, false, false);
                }

                continue;
            }

            switch (action.Kind)
            {
                case ActionKind.Bet when !betSeen:
                    betSeen = true;
                    cbet = action.Player == preflopRaiser;
                    break;
                case ActionKind.Bet or ActionKind.Raise:
                    // Raised before the hero answers: he faces a raise, not the c-bet.
                    return (false, false, false);
            }
        }

        return (false, false, false);
    }

    private static (bool Opportunity, bool Made) CheckRaise(List<HandAction> flop, string hero)
    {
        var heroActions = flop.Where(a => a.Player == hero).ToList();
        if (heroActions.Count < 2 || heroActions[0].Kind != ActionKind.Check)
        {
            return (false, false);
        }

        // After a check, acting again means a bet came in.
        return (true, heroActions[1].Kind == ActionKind.Raise);
    }

    private static (bool Opportunity, bool Made) SecondBarrel(List<HandAction> flop, List<HandAction> turn, string hero, bool heroCbetFlop)
    {
        if (!heroCbetFlop || flop.Any(a => a.Kind == ActionKind.Raise) || !turn.Any(a => a.Player == hero))
        {
            return (false, false);
        }

        foreach (var action in turn)
        {
            if (action.Player == hero)
            {
                return action.Kind is ActionKind.Bet or ActionKind.Check
                    ? (true, action.Kind == ActionKind.Bet)
                    : (false, false);
            }

            if (action.Kind is ActionKind.Bet or ActionKind.Raise)
            {
                // Someone bet into him: no chance to barrel.
                return (false, false);
            }
        }

        return (false, false);
    }
}
