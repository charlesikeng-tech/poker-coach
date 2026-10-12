# ADR-0020 — Preflop sizing: reference sizes, drill and the player's own sizes

- Status: Accepted
- Date: 2026-10-12
- Extends ADR-0009 (references, not GTO), ADR-0018 (defence against an open) and ADR-0006 (hand facts).

## Context

Players asked for "the right sizing against villain". The phrase covers three different things:

1. **Sizing for the spot**: the opener's position, the stacks, whether the raiser will be in position, how many
   players are in. These are stable rules.
2. **Sizing for one opponent's profile**: an exploit that needs that opponent's statistics.
3. **Sizing for the population** ("low stakes call too much"): heuristics, not right-or-wrong answers.

The second was ruled out for now:

- **Samples are too thin.** In €5–€20 MTTs with large fields, a player rarely sees the same opponent for more
  than a few dozen hands, and a recommendation built on 15 hands is noise.
- **The data model would change.** Hand facts are the hero's only; storing every seat's facts multiplies the
  rows by 6 to 9.
- **The privacy promise would change.** Today we promise that opponents' names never leave our servers;
  profiling them is a different promise.

Profile-based sizing belongs with the future HUD, which reads live data from the table being played. Population
heuristics belong in the AI explanations, not in a graded drill. Postflop sizing (c-bet by board texture) comes
later: it needs a board classification, and without a solver its references are weaker than preflop ones.

## Decision

**Scope v1: preflop, in two spots.** The two spots are opening when folded to, and 3-betting a single open,
which is the same narrow spot as the defence ranges: one raise, not all-in, nobody limping or calling before the
hero. Squeezes are left out.

**Reference sizes v1** (`ReferenceSizings`) are our conventions for low-stakes online MTTs with antes, chip EV,
no ICM, labelled "repère" and never GTO. There is one size per spot, whatever the hand, so the size gives nothing
away. Each reference is a best size plus an interval that is still in line.

- **Open:**

  | Stack | Seat | Best size | In line |
  |---|---|---|---|
  | Below 15 BB | any | all-in (push/fold) | all-in only |
  | 15–25 BB | not the small blind | 2 BB | 2–2.2 BB |
  | 25–40 BB | not the small blind | 2.2 BB | 2–2.3 BB |
  | 40 BB and more | not the small blind | 2.2 BB | 2–2.5 BB |
  | Below 25 BB | small blind | 2.5 BB | 2.5–3 BB |
  | 25 BB and more | small blind | 3 BB | 2.5–3.5 BB |

  The small blind raises bigger because it plays the hand out of position.
- **3-bet:**
  - In position: ×3 the open, in line from ×2.5 to ×3.3.
  - Out of position: ×4 the open, in line from ×3.5 to ×4.5.
  - The blinds are out of position, except the big blind against the small blind.
  - **All-in** below 25 BB effective, or when the raise would put more than a third of the effective stack in
    (the commit rule). Past that point the raiser is committed anyway, so the shove takes the most fold equity.
- **Judging a played size:** it is rounded to a tenth first, so a 2.25 BB open counts as 2.2–2.3 BB and is not
  called a mistake.

**Drill** (`SizingDrill`, training room "Sizing" mode, Pro offer like the other drills):

- The decision to raise is given. The hand shown is drawn from the reference range of the spot: the opening
  range, or the defence reference's 3-bet range (its 25 BB version stands in below 25 BB).
- The question is the size: 5 choices for an open, 6 for a 3-bet (multiples of the open). Any size inside the
  interval is right.
- Spots:
  - Stacks are drawn inside the chosen band (short, mid or deep). Every stack is equal, so the effective stack
    is the one shown.
  - Opens faced: 2, 2.2 or 2.5 BB, and 2.5 or 3 BB from the small blind.
  - 3-bet pairs: those with a written 3-bet reference.
- Missed spots come back, as in the other drills. Answers are stored in `training.sizing_attempts` (their own
  table: the answer is a size, and the spot carries an opener and an open size) and count toward the weekly
  goal.

**The player's own sizes** (hand facts version 5):

- Six new facts:
  - the open's raise-to in big blinds (blinds included, antes not) and whether it was all-in;
  - in the facing-open spot, the open faced and the effective stack (the smaller starting stack of the hero and
    the opener);
  - the 3-bet's raise-to and whether it was all-in.
- Bumping the facts version recomputes every stored hand in the background.
- `GET /api/training/sizing/played` judges each raise with its own stack and open faced, then groups the raises:
  opens by seat (named by distance to the button) and band, 3-bets by in or out of position and band.
- Each group shows: the number of hands, how many are in line, too small or too big, the median size with
  all-ins left out, the reference most of its hands called for, and links to the three latest hands off the
  reference.

## Alternatives considered

- **A size per hand (polarised or linear sizing).** It is closer to some solver outputs, but it is harder to
  learn, and it leaks the hand to observant opponents. One size per spot is the low-stakes standard.
- **Free-form size input.** It is more realistic, but grading a continuous answer means explaining the interval
  anyway. Fixed choices make the decision fast and comparable, and the interval accepts neighbouring sizes.
- **Opponent profiles from the player's own database.** Ruled out for the reasons above. To revisit with the HUD.
- **Columns on `opening_attempts`.** They would make every range-drill row carry nullable sizing fields and blur
  the answer type.

## Consequences

- Statistics show "hands being analysed" for a while after deployment (facts version 5).
- Rules to maintain: changing a size or an interval bumps `ReferenceSizings.Version`, and attempts checked
  against an older version stop counting as reviews or progress.
- Follow-ups:
  - squeeze sizing (+1 open size per caller);
  - postflop c-bet sizing by board texture;
  - a "sizing" leak in leak detection once the measured gaps prove stable;
  - opponent-aware sizing with the HUD.
