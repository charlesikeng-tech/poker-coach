# ADR-0017 — AI usage guard: credits, real cost and caps before every provider call

Status: accepted (2026-10-12). Replaces the coaching gate of ADR-0008 / ADR-0016; credit prices, plan limits
and the global monthly budget are unchanged.

## Context

With three paid offers (ADR-0016), the AI provider is the one cost that grows with use. Every call went through
a gate (plan, credits, daily limit, budget) and was then recorded in `coaching.model_usage`. Reviewing it
before launch showed four holes:

1. **Failed calls were not recorded.** An answer rejected by our checks, cut by `max_tokens`, or still
   rejected after the correction attempt is billed by the provider, and the correction resends the whole
   prompt. None of it reached the ledger, so the budget under-counted real spend.
2. **Check, then record, with nothing in between.** Two requests read the usage before either recorded. Twenty
   debriefs fired at once from several tabs all passed the check: a Starter with 3 credits could spend 15. A
   test on PostgreSQL showed 8 of 20 concurrent requests passing for a 3-credit allowance.
3. **The budget only looked back.** "Spent ≥ cap" ignored the call about to be made.
4. **The check depended on discipline.** Each service called the gate, then the model, then remembered to
   record. A new AI feature forgetting one step bypassed everything.

## Decision

**One guard, the only way to the provider.** `AiUsageGuard.RunAsync(user, task, work)` in the Coaching module.
Services hand it a `work` delegate that prepares the prompt and calls the model; they never call the model
otherwise. Not a separate module: the coach is the only consumer, and a module boundary with one client adds
indirection without protecting anything. If another module calls the AI one day, the guard moves with its
ledger, not the rules.

**Reserve, call, settle.**

1. *Before*, in one short transaction holding a PostgreSQL advisory lock (`pg_advisory_xact_lock`, one key
   for all reservations): read the user's credits this month, calls of the task today and this month, the
   month's spend; decide (plan, credits, Pro's monthly review, daily limit, budget room for the worst case);
   insert a `Reserved` row carrying the task's credits and the call's **ceiling cost**. The lock is held for
   a few indexed reads and one insert, never during the provider call. Refusals cost no preparation: the
   prompt is built only after the reservation.
2. *During* the provider call, no transaction is open.
3. *After*, the row is settled, with a token that ignores the request's cancellation so the cost always
   lands:
   - success → `Billed`, real cost, credits kept;
   - provider failure with known usage (rejected answer, `max_tokens`, failed correction) → `Failed`,
     **real cost recorded**, credits given back to the player;
   - unknown usage (unreachable, timeout, unexpected exception, client cancellation) → `Failed`, cost =
     the reserved ceiling, `usage_known = false`. Never a guess below what may have been billed.
   - An error status (4xx/5xx) is not billed by the provider: zero cost.

**The ceiling** comes from the adapter (`ICoachingModel.Ceiling(task)`), which owns `max_tokens` and the
correction policy: every attempt at full output, every input at the cache-write rate, a correction resending
the rejected answer. Debrief ≈ 0.42 $, explanation or review ≈ 0.20 $ at current prices. It only matters near the
end of the budget; the real cost replaces it at settlement.

**Abandoned reservations** (process killed mid-call) need no background job: after 10 minutes a `Reserved`
row stops counting for credits and limits, but its ceiling keeps counting in the month's spend, since
whether the provider billed it is unknown.

**What counts where**

| | credits | daily limit | Pro monthly review | budget |
|---|---|---|---|---|
| Billed | yes | yes | yes | real cost |
| Failed | no | yes (a failure loop still costs) | no | real cost, or ceiling if unknown |
| Reserved, live | yes | yes | yes | ceiling |
| Reserved, abandoned | no | no | no | ceiling |

**Observability.** A warning is logged when a reservation pushes the month across 80 % of the budget, and
on every refusal for exhausted budget. `GET /api/admin/ai-usage` and the admin page show the month's spend
against the budget, open reservations, calls / failures / unknown-cost calls per task, **cost per credit**
per task (the figure to compare with the offers' prices), and the ten heaviest users.

**Migration `AiUsageGuard`** adds `status`, `credits`, `reserved_cost_usd`, `usage_known`, `settled_at` to
`coaching.model_usage`; existing rows become `Billed` with their ADR-0016 credits. Credits are now read from
the row, so a future change of credit prices does not rewrite history.

## Alternatives considered

- **Serializable isolation instead of a lock.** Correct, but turns contention into retried transactions and
  serialization errors to handle; the advisory lock is explicit and its scope obvious.
- **Per-user lock only.** Protects credits, not the global budget, which is the real money risk.
- **A counter column per user and month, updated atomically.** Needs a second source of truth kept in sync
  with the ledger and a monthly reset; the ledger already has every fact.
- **Redis or an external quota service.** Infrastructure we do not run, for a volume PostgreSQL handles.
- **Decorator on `ICoachingModel`.** Would hide the user and task it needs; an explicit call is clearer.

## Consequences

- One lock serialises all AI reservations: a few milliseconds each, far beyond our volume. Revisit (per-user
  lock plus a budget row) only if reservations show up in latency.
- A player whose call fails keeps their credits; the platform pays. Visible per task on the admin page.
- Cancellation by the client counts at the ceiling: conservative, and rare.
- The budget (`Coaching:MonthlyBudgetUsd`) still has to be raised by hand as subscribers grow; the admin page
  and the 80 % warning say when.
