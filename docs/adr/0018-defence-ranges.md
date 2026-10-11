# ADR-0018 — Defence against an open: call and 3-bet ranges

- Status: Accepted
- Date: 2026-10-12
- Extends ADR-0009 (ranges: sources and naming) and ADR-0006 (hand facts).

## Context

The Ranges page shows what a player opens. The other half of preflop, and the most common low-stakes leak
after opening, is what he does **facing an open**: over-folding the big blind, flatting too wide out of
position, never 3-betting. Players asked for "call ranges". Two spots answer to that name: calling a shove
(push/fold, already solved by `PushFoldNash`) and defending against a normal open; the second was chosen.

ADR-0009's rule applies: no chart copied from a tool or the web, references are ours and say so, "GTO" only
for what we compute. A preflop solver for 25–40 BB with antes is still out of scope.

## Decision

**The spot.** A new hand fact (`HeroHandFacts` version 4): the hero's first preflop decision faces **exactly
one raise, not all-in, with nobody having limped or called before him**. Stored: the opener's position
(`facing_open_from`), and whether he called or 3-bet (otherwise he folded). Narrower than the 3-bet
opportunity on purpose: squeezes (a caller in between) and open-shoves (push/fold) are different strategies.
Bumping the facts version recomputes every stored hand in the background, as for previous versions.

**Counts.** `GET /api/ranges/defence?format&band&defender&opener&from&to` returns every defender–opener pair of
the format with its counts (spots, calls, 3-bets, defence and 3-bet rates) and, for the selected pair, the 169
hands with what the player did and what the reference does. One pair's cells at a time: all pairs at once
would be ~6,000 cells at full ring. Seats are named like the opening ranges (`OpeningSeat.Canonical`); unread
hero cards are left out, never guessed. Plan: Pro, like the opening ranges (`Feature.DefenceRanges`).

**Reference v1** (`ReferenceDefenceRanges`): our conventions, written hand by hand as call / 3-bet / fold, for
low-stakes online MTTs with antes, an open of 2–2.3 BB (3 BB from the small blind), chip EV, no ICM. Labelled
"repère" / "reference", never GTO.

- **Depth: 25 BB and more** (mid and deep bands share it). Between 15 and 25 BB, 3-bets become shoves and flats
  shrink: a different strategy. The page shows the player's counts there and says no reference is written yet,
  rather than showing a wrong one.
- **Spots written:** big blind and small blind against every opener, button against UTG–CO, cutoff against
  UTG–HJ, a middle seat (LJ/HJ) against an early or middle open. Openers are grouped: early (UTG–UTG+2),
  middle (LJ–HJ), CO, BTN, SB. Seats go by distance to the button, so a 6-max UTG opens and defends like a
  full-ring lojack. Other pairs (UTG+1 facing UTG…) have counts but no reference.
- **3-bet bluffs** are limited to suited wheel aces, so a grid reads at a glance.
- Overall shares (deep): BB defends ~21 % of hands against an early open, ~60 % against the button and the
  small blind; the small blind mostly 3-bets or folds. Tests check that no hand is both called and 3-bet,
  that premiums are always 3-bet, and that the big blind widens as the opener moves later.

**Web.** An "Open / Défense vs open" switch on the Ranges page. Same 13×13 grid and views as the openings: the
player's grid (3-bet share in gold, call share in blue), the reference, and the gap (played where the
reference folds, folded where it plays, played the other way, as the reference). A hand counts as played when
called or 3-bet at least half the time, then by the more frequent action.

## Alternatives considered

- **Computing defence from pot odds and equity against our reference opening ranges.** Reproducible, but the
  result hinges on equity-realisation factors (position, suitedness) that are guesses dressed as maths; a
  written reference states its assumptions more honestly. Worth revisiting with a solver.
- **Defence against any raise (squeezes, cold 4-bets).** More spots, but grids that mix different
  strategies; the single-open spot covers most hands played.
- **Charts from published sources.** Rejected by ADR-0009 (licence, unknown assumptions).

## Consequences

- One more hand-facts version: statistics show "hands being analysed" for a while after deployment.
- Short-stack defence (15–25 BB, 3-bet shove ranges) and squeeze spots are follow-ups; a defence drill in the
  training room can reuse the reference.
- The reference is ours to maintain: changes bump `ReferenceDefenceRanges.Version`.
