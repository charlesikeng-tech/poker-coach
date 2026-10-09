# Poker Coach — feature map and roadmap

Living document: what exists, what could make Poker Coach the all-in-one tool of the MTT player, and the
order agreed on 2026-10-09. Effort: S (days), M (a week or two), L (more, or a decision first).

## Shipped

- **Account**: Google sign-in (ADR-0004), sign-in page with the welcome intro, FR/EN/ES, light/dark.
- **Import**: Winamax hand histories and summaries by upload (ADR-0002/0005): re-entries, rebuys, bounties,
  late registration, idempotent, pseudonym confirmation.
- **Tournaments**: list + detail (stack curve, key moments, session stats), search.
- **Performance**: profit, ROI, ITM, curve, breakdowns by buy-in, type, speed. **Dashboard**.
- **Statistics**: per position, table format, stack band, complete histories (ADR-0006, ADR-0009).
- **Leaks**: reference ranges + Wilson guard (ADR-0007); AI explanations (ADR-0008, Sonnet, $20/month cap).
- **Replayer**: animated hand replay, key-moment navigation.
- **Ranges** (ADR-0009): actual range, references v1, hand-by-hand gap, 6-max / full ring, push/fold
  equilibrium below 15 BB.
- **Training**: open-or-fold and push-or-fold drills, spaced repetition, progress per seat.

## Ideas by domain

1. **Import & data**: desktop companion auto-import (L, Avalonia vs Flutter decision); other rooms —
   PokerStars, GGPoker, Betclic, PMU (M each); Spins/Expresso, cash games (M); data export and account
   deletion (S).
2. **Analysis**: postflop statistics (M, high priority); all-in EV / luck (S preflop, M postflop); stats by
   tournament stage (M); ICM pressure (L); time and volume tendencies (S); opponent profiles (M, ADR first).
3. **AI coach** (explains, never measures): session/tournament debrief (M), weekly report (M),
   "why this spot" on a replayed hand tied to a measured leak (M). Free chat: not now (cost, invention).
4. **Training**: big blind defence vs shove (S, calls already computed); quiz on own missed spots (M);
   defence vs open and 3-bet (M, references or solver); postflop (L, solver ADR).
5. **Ranges**: call-vs-shove charts (S); 3-bet/defence references (M); ICM push/fold (L); range editor (M).
6. **Money**: bankroll with deposits/withdrawals (S); buy-in limit and alerts (S); risk of ruin and
   downswing simulator from the player's own results (M); ROI with its uncertainty (S).
7. **Progress**: weekly plan from leaks (M); leak evolution over time (S); training streaks (S).
8. **Social**: anonymised hand sharing link (S); coach–student access (M); study groups (to discuss).
9. **At the table** (needs the desktop companion): HUD (L); session timer, break and tilt reminders (M);
   opponent notes (M).
10. **Platform**: deployment and observability (M, before opening to other players); installable web app
    (M); subscriptions and AI quotas per plan (L).

## Agreed order

1. ✅ Money: bankroll, buy-in limit, risk of ruin, ROI uncertainty. Replaced the empty Sessions page;
   session tendencies move to Performance (still to do).
2. All-in EV (preflop), then big blind defence in the trainer.
3. Postflop statistics (and the leaks they unlock).
4. Weekly plan (Progress).
5. Deployment when other players test the app.
6. Large projects: desktop companion, other rooms, HUD.
