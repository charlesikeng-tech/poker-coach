# ADR-0006 — Hand statistics: per-hand hero facts, computed in the background, aggregated in SQL

- Status: Accepted
- Date: 2026-10-09

## Context

MVP-2 analyses how the player plays: VPIP, PFR, 3-bet, steal, c-bet, showdown, by position and stack
depth. A regular has tens of thousands of hands; filters change at every click; definitions will be
refined (and new statistics added) many times.

## Decision

**Domain analysis.** `HandAnalyzer` (Domain, pure) turns one hand (`HandForAnalysis`, provider-neutral)
into `HeroHandFacts`: the hero's position, stack in big blinds, and one *made* flag plus one
*opportunity* flag per statistic. A rate is always made / opportunities, never made / hands.

Definitions (usual tracker conventions):

| Statistic | Opportunity | Made |
|---|---|---|
| VPIP / PFR | the hero acted voluntarily preflop (walks and all-in blinds excluded) | call or raise / raise |
| Open (RFI), Limp | everyone before folded | raise / call |
| Steal | open spot from cutoff, button or small blind | raise |
| 3-bet | facing exactly one raise, first decision | re-raise |
| Fold to 3-bet | the hero opened and faces a single re-raise | fold |
| Flop c-bet | last preflop raiser, saw the flop, checked to or first to act | bet |
| Went to showdown | saw the flop | still in with 2+ players at the end |
| Won at showdown | went to showdown | collected chips |

Positions: blinds are whoever posted them; between the big blind and the button, players are named
from the button backwards (cutoff, hijack, lojack, UTG+2, UTG+1), the first one always UTG; heads-up the
button is the button.

**Stored, versioned facts.** One row per hand in `poker.hand_hero_facts`, tagged with
`HeroHandFacts.Version`. Statistics are a single SQL aggregate (`COUNT … FILTER` per flag, grouped by
position) over these rows: no hand is loaded into memory to answer a query.

**Computed in the background.** The import worker, when its queue is empty, processes hands whose facts
are missing or older than the current version (`HandFactsBackfill`, batches of 500). New imports and
definition changes take the same path: bump the version and every hand is recomputed from the stored
hand document (`poker.hands.details`). The API reports how many hands are still pending; the web page
refreshes until it reaches zero.

## Alternatives considered

| Option | Why not |
|---|---|
| Compute on the fly from `details` at each request | Loads every hand per click; seconds at 50k hands. |
| Compute in the import transaction only | A definition change would need a separate migration path anyway; one path is simpler. |
| Materialized views / SQL-only analysis | Poker logic (positions, action order) in SQL is hard to test and to evolve. |
| A columnar/OLAP store | Not needed at this volume; PostgreSQL aggregates on narrow rows are fast enough. |

## Consequences

- Statistics lag imports by a few seconds (shown as "analyzing N hands").
- Changing a definition = code change + version bump; the backfill recomputes everything.
- Only hero statistics for now; opponents' statistics (for a HUD) would need per-player facts and an
  opponent-data retention decision first.

## Hand coverage (amendment, same day)

Per tournament, `TournamentCoverage` (Domain) is computed in the background once the tournament's
hand facts are current, stored in `poker.tournament_coverage` (versioned, recomputed when hands are
added or a newer summary arrives):

- **hand-number gaps** per table, read from the room's hand ids through the provider
  (`IHandHistoryProvider.TryReadTableSequence`; Winamax ids are `table-number-timestamp`);
- **stack breaks**: the hero's stack at a hand differs from the previous hand's end (start + net);
  a bust followed by a new stack is a **re-entry**, not a break;
- **start missing**: registered on time (summary) but first hand past level 1; unknown with late
  registration; **end seen**: last hand ends at 0 chips, or the summary says he won.

Complete = no gap, no break, end seen, start not known missing, entries seen = summary entries.

## Not covered yet

- Per-entry attribution of hands (re-entries) for statistics by entry.
- Postflop beyond flop c-bet and showdown; all-in adjusted results.
