# ADR-0012 — Coach reports: tournament debrief and weekly review

Status: accepted (2026-10-10). Extends ADR-0008 (provider, cost, data that leaves the platform).

## Context

The AI coach only explained leaks. Before opening to test players, two moments where a coach matters
most: right after a tournament ("what happened, what do I take from it") and at the end of a week of the
plan ("is it working, what next"). Free chat stays out (unbounded cost, the model drifting into
measuring). Both new texts follow ADR-0008: **the model explains, it never measures**.

## Decision

**Tournament debrief** (`GET|POST /api/tournaments/{id}/debrief`). Input, all computed by us: buy-in,
field, type and speed, entries, finish and money (or "unknown, do not guess" without a summary), duration,
stack start/peak/end in BB, this tournament's statistics (labelled descriptive, never leaks; only rates
with 5+ spots), the player's confirmed long-term leaks (90 days, to link a moment to only when the hand
shows it), and the key moments (ADR tournament detail: hands moving a quarter of the stack or more), each
with its anonymous story (positions, BB, actions, board, cards shown at showdown) and, for preflop
all-ins with every hand shown, our computed equity. Output (JSON schema): headline, story, a verdict and
a note per commented moment (`well_played`, `mistake`, `variance`, `standard`), strengths, things to
work on.

**Verdicts are checked against our figures.** "Variance" survives only when the computed equity shows the
favourite lost (or the underdog won); otherwise it becomes "standard". The model may cite only the
moments it was given, once each. Not available while facts are being computed (409 `HANDS_PENDING`) or
under 20 hands dealt (422 `NOT_ENOUGH_HANDS`).

**Weekly review** (`GET|POST /api/progress/review?week=`), this week (a mid-week check) or a past week of
the history. Input: the plan's priorities with baseline, reference and this week's measured rate and
status (`ProgressService`; the status is the truth, "not judged" below 10 spots), volume, drills against
the goal, and the week's money with an explicit "mostly variance, judge nothing from it". Output: headline,
summary, a note per priority, next steps. Under 30 hands in the week: 422.

**Stored, reused, regenerated on demand.** One report per user, kind, subject (tournament id, week's
Monday) and language in `coaching.reports`, replaced on regeneration (upsert on that key). Its
fingerprint records the inputs (tournament: hands, pending, result, finish, facts version; week: hands,
spots per priority, drills by tens, results, week over, plan date; plus the prompt version and language).
`GET` is free and says `stale: true` when the inputs moved; only `POST` calls the model, and returns the
stored report unpaid when it is still current. The player decides when an update is worth it — no
automatic regeneration after each import.

**Same gate for every paid call** (`CoachingGate`): model configured, per-feature daily limit (debriefs
10, reviews 5, explanations 20), monthly budget shared by all features ($20 in beta), every call in the
usage ledger even when the answer is then rejected. Estimated cost: ≈ $0.03 per debrief (≈ 7k tokens in,
1.2k out on Sonnet), ≈ $0.01 per review.

**Port.** `ICoachingModel` gains one method per task (`DebriefTournamentAsync`, `ReviewWeekAsync`), each
with its own instructions and schema in the adapter; a shared voice block keeps one coach across tasks
(informal address, English poker terms, "leak" never translated). One method per task rather than a
generic "prompt in, JSON out": each task's validation stays typed and explicit.

**Privacy.** Nothing new identifies anyone: no tournament name, no date, no pseudonym; opponents by
position. Reports are exported with the account and deleted with it (FK cascade). The privacy page says
so.

## Alternatives considered

- **Generate debriefs automatically after each import.** Nicer, but pays for tournaments nobody reads
  and multiplies cost by volume. On demand first; reconsider with usage data (ADR-0011 feature usage).
- **Let the model judge every hand freely.** Without a solver its verdicts are opinions; only the one
  verdict we can check with our own numbers (variance) is enforced, the others are framed as coaching.
- **Keep history of every regenerated report.** No user need yet; one current report per subject.

## Consequences

- An evaluation set (ADR-0008) now needs debrief and review cases as well before prompt changes.
- Bumping a prompt's `Version` marks every stored report of that kind as stale (free to read, paid to
  rewrite).
