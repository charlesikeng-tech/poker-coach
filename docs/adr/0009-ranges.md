# ADR-0009 — Ranges: sources, naming and order of delivery

- Status: Accepted
- Date: 2026-10-09

## Context

Players want a Ranges section: see what to open from each position, compare with what they actually
open, and learn it. The hard question is not the grid UI but **where ranges come from** and **what we may
call "GTO"**. Competitors show "GTO" tables without a verifiable source; doing the same would break the
product's rule that every figure shown is measured or sourced (the model explains, it never measures).

## Decision

**Sources.**

- No chart copied from commercial tools or found online: they are licensed or of unknown origin
  (ante structure, depth, rake), and we could not defend their content.
- **Reference ranges** are ours, versioned and documented like the leak references (ADR-0007), and
  named "references", never "GTO". They state their assumptions (low-stakes online MTT, antes, stack
  band).
- **"GTO" is reserved for what we compute and can reproduce**: push/fold equilibria below 15 big blinds
  (chip EV, antes), from a precomputed hand-versus-hand equity table and an equilibrium algorithm in the
  Domain, with tests. Deep-stack preflop solutions (multi-way, 40 BB with antes) need a preflop solver:
  out of scope until a licensed or self-run solver is decided in its own ADR.

**Order of delivery.**

1. **Your actual range**: for each position, how often the player opens each of the 169 starting hands
   when folded to (raise-first-in), from his own hands, with the sample per hand. Compared with the
   position's reference opening rate (ADR-0007). No external source needed.
2. Reference opening ranges per position and stack band, as 13×13 grids (version 1), then per-hand
   comparison with the actual range.
3. Trainer: open-or-fold quiz with spaced repetition, results per user, focused on positions with a
   detected leak.
4. Push/fold equilibrium below 15 BB, the only block labelled GTO.

**Model.** A starting hand is a `HandClass` (13 pairs, 78 suited, 78 offsuit), placed on the usual grid:
pairs on the diagonal, suited above, offsuit below. Frequencies are opens / times dealt in an RFI spot;
a cell with few occurrences is shown as such (count visible), never extrapolated. Aggregation is done in
SQL per position and exact two-card holding, then classified in memory (at most 1,326 × 9 rows).

## Consequences

- No licensing risk; every number in the section traces back to the player's hands, our versioned
  references, or our own computation.
- The section starts without deep-stack "GTO" grids, which some players expect. Accepted: an honest
  reference beats an unverifiable one, and the actual-range view is something competitors do not offer.
- Adding a solver later is a new ADR (cost, licence, compute) and does not change the model.
