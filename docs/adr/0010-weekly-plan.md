# ADR-0010 — Weekly plan

Status: accepted (2026-10-09)

## Context

Statistics, leaks, ranges and drills each answer one question. A player who opens the app needs one
answer: what do I work on this week, and is it working? (Roadmap step 4.)

## Decision

**A plan per user and week, frozen once made.** Built on first read of the week from the leaks of the
last 90 days (recent play, not last year's player), in the format the player plays most: up to three
priorities, in the leak report's order (confirmed first, then furthest from the range), two seats of
the same statistic at most. Stored in `progress.weekly_plans` (key: user, week): the key makes "one
plan per week" a database rule, so a first read from two tabs cannot create two plans.

Frozen because priorities that moved with every import would leave nothing to work towards. The
player can rebuild it explicitly (after a big import).

**Progress is measured live**, not stored: each priority's rate over the week's hands (15+ BB, same
format) against its range and its baseline — in range, improving (closer than the baseline), off track,
or not enough spots (fewer than 10: shown, not judged). Past weeks are measured the same way on their
own hands.

**No plan below 300 recent hands** (15+ BB): it would rest on noise. Nor while hands are still being
analysed.

**What to do:** each priority links to its training-room drill when one exists (opening leaks: RFI by
seat, steal, limp, VPIP/PFR); the others to the leak and its coach. A weekly drill goal (100 answers)
gives a concrete target.

**Weeks start Monday 00:00 UTC.** No time zone is stored per user yet; for European night sessions the
cut falls at 01:00–02:00 local, after play. To revisit when players outside Europe arrive.

**Building on a GET.** The first read of the week stores the plan. It is idempotent (one plan per week,
`ON CONFLICT DO NOTHING`), so the endpoint keeps safe-method semantics for the client; the rebuild,
which deletes, is a POST with the anti-forgery check.

## Alternatives considered

| Option | Why not (now) |
|---|---|
| Recompute priorities on every read | They would change mid-week with imports: no stable goal. |
| Store weekly snapshots of every statistic | Derivable from the hands at any time; storage and sync for nothing. |
| A scheduled job building plans on Mondays | Infrastructure for a lazy, cheap computation; plans for inactive players. |
| Player-chosen priorities | Later: suggestions first, choice once the suggestions are trusted. |

## Consequences

- A reference-range change (ADR-0007) applies to next week's plan; the stored plan keeps its own
  version.
- Leak texts are shared with the Leaks page: one wording per leak.
