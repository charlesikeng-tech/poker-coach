# Poker Coach — feature map and roadmap

Living document: what exists, what could make Poker Coach the all-in-one tool of the MTT player, and the
order agreed on 2026-10-09. Updated 2026-10-21. Effort: S (days), M (a week or two), L (more, or a decision first).

## Shipped

- **Account**: Google sign-in + email/password (ADR-0004, ADR-0013), sign-in page with the welcome intro,
  FR/EN/ES, light/dark. Data export (zip) and account deletion. Password recovery.
- **Import**: Winamax hand histories and summaries by upload (ADR-0002/0005): re-entries, rebuys, bounties,
  late registration, idempotent, pseudonym confirmation. Parsers PokerStars, Betclic, PMU (présents, rooms à valider).
- **Tournaments**: list + detail (stack curve, key moments, session stats), search.
- **Cash sessions**: list, detail, performance (BB/100, profit), debrief IA.
- **Performance**: profit, ROI, ITM, curve, breakdowns by buy-in, type, speed. **Dashboard**.
- **Statistics**: per position, table format, stack band, complete histories (ADR-0006, ADR-0009).
  Postflop détaillé : c-bet flop/turn, fold to c-bet flop/turn, raise c-bet flop, check-raise flop,
  donk bet flop, probe bet turn, WTSD/W$SD/WWSF, aggression postflop — avec leaks et ranges de référence.
  Trend par mois. All-in EV / luck preflop (courbe, swings).
- **Leaks**: reference ranges + Wilson guard (ADR-0007); AI explanations (ADR-0008, Sonnet 5.5, $20/month cap).
- **Replayer**: animated hand replay, key-moment navigation.
- **Ranges** (ADR-0009): actual range, references v1, hand-by-hand gap, 6-max / full ring, push/fold
  equilibrium below 15 BB. Defence ranges (ADR-0018).
- **Training**: open-or-fold and push-or-fold drills, spaced repetition, progress per seat.
  **BB defence vs shove** (call/fold, ranges calculées). **Sizing drills** (ADR-0020): open-raise et 3-bet
  par position et stack, choix multiples, review des spots ratés, quiz sur ses propres mains mal sizées,
  analyse des raises jouées vs référence.
- **AI coach**: explications de leaks (1 crédit), revue hebdomadaire (2 crédits), débrief tournoi (5 crédits),
  débrief cash game, conseil bankroll. Guard pattern Reserve/Call/Settle (ADR-0017), caching SHA-256.
- **Money**: bankroll avec dépôts/retraits, buy-in limit (prudent/standard/agressif), risk of ruin et
  simulateur de downswing (Monte Carlo), ROI avec intervalle de confiance 95 %.
- **Progress**: plan hebdomadaire depuis les leaks (WeeklyPlanner), progression live, historique par semaine.
  Training streaks.
- **Social**: accès coach–élève (invitations, lecture seule, page coach avec onglets Leaks/Stats/Progression).
- **Subscriptions**: plans Starter/Pro/Elite, crédits IA par plan, Stripe checkout, changement de plan, top-up.
- **Platform**: CI (GitHub Actions: backend + web + image Docker + smoke test), GHCR, Scaleway (ADR-0019),
  Caddy edge, health checks. Notifications hebdomadaires par email (Brevo). Calculateurs ICM, Pot Odds,
  M-Ratio, Équité des ranges. Landing page FR/EN/ES. Sentry front + back. Changelog automatique (git-cliff).

## Ideas by domain

1. **Import & data**: desktop companion auto-import (L, Avalonia vs Flutter decision); other rooms —
   valider et compléter PokerStars, Betclic, PMU (S–M each); GGPoker (M); Spins/Expresso (M).
2. **Analysis**: ~~postflop statistics détaillées~~ (shipped); all-in EV postflop (M); stats by
   tournament stage (M); ICM pressure (L); time and volume tendencies (S); opponent profiles (M, ADR first).
3. **AI coach**: "why this spot" on a replayed hand tied to a measured leak (M). Free chat: not now (cost, invention).
4. **Training**: defence vs open and 3-bet (M, references or solver); postflop (L, solver ADR).
5. **Ranges**: call-vs-shove charts (S); ICM push/fold (L); range editor (M).
6. **Progress**: leak evolution over time (S).
7. **Social**: anonymised hand sharing link (S); study groups (to discuss).
8. **At the table** (needs the desktop companion): HUD (L); session timer, break and tilt reminders (M);
   opponent notes (M).
9. **Platform**: CD automatisé (GitHub Actions → Scaleway SSH) (M); installable web app (M).

## Agreed order

1. ✅ Money: bankroll, buy-in limit, risk of ruin, ROI uncertainty.
2. ✅ All-in EV (preflop), ✅ BB defence vs shove in the trainer, ✅ sizing drills (open + 3-bet).
3. ✅ Postflop statistics détaillées (fold to turn c-bet, donk bet, probe bet + leaks).
4. ✅ Weekly plan (Progress).
5. Deployment automatisé quand d'autres joueurs testent l'app.
6. Large projects: desktop companion, other rooms, HUD.
