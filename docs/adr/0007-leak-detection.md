# ADR-0007 — Leak detection: reference ranges and a statistical guard

- Status: Accepted
- Date: 2026-10-09

## Context

MVP-3 tells the player where his game goes wrong. A leak is a statistic outside what sound players do,
judged on enough situations that variance alone does not explain it. Two questions: what is "sound"
(the reference), and when is a sample big enough (the guard).

## Decision

**References: fixed ranges first, adjustable later, platform-derived eventually.** Version 1
(`ReferenceRanges.LowStakesMtt`, Domain) holds usual figures for solid regulars in online MTTs from €5 to
€20, mostly 6-max: overall VPIP, PFR, VPIP−PFR gap, limp, steal, 3-bet, fold to 3-bet, flop c-bet,
WTSD, W$SD, and the opening range (RFI) by position. They are conventions, documented as such in the
product ("references version 1"), not truths. Next steps, in order: let the player adjust them; derive
them from the platform's own winning players once there are enough.

**Only hands with 15 big blinds or more.** Below, play is push/fold and these references do not apply.

**Statistical guard.** A statistic is judged only with at least 30 opportunities (otherwise it is
listed as "not judgeable yet"). A leak is **confirmed** when the whole 95 % Wilson interval of the rate
lies outside the range; **to watch** when the rate is outside but the interval still overlaps it.
Wilson rather than the normal approximation: honest at small samples and near 0 % / 100 %.

**Explanations.** Each statistic and direction has a short, translated text: why it costs money and
what to work on. AI-written explanations with example hands come later (provider and cost decision
pending: ADR to come).

## Alternatives considered

| Option | Why not (now) |
|---|---|
| Player-defined ranges only | Asks the player to know the answer before getting any value. |
| Platform averages now | One user: no population yet. |
| Fixed minimum sample per statistic, no interval | Arbitrary; flags variance as leaks or hides big ones. |
| Leaks by position for every statistic | Most position samples are too small for months; RFI is where position matters most. |

## Consequences

- Changing a range = code change + `ReferenceRanges.Version` bump (shown to the player).
- The result depends on stack depth filtering and on coverage; the page states its basis (hands,
  tournaments, complete histories).
