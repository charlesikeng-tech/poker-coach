# ADR-0016 — Three offers: Starter, Pro and Elite

Status: accepted (2026-10-12). Supersedes the Free/Pro grid of ADR-0014; billing mechanics (Stripe
Checkout, portal, webhooks re-reading the customer, plan derived rather than stored) are unchanged.
Extends ADR-0015 (offered access now names an offer).

## Context

ADR-0014 shipped one paid plan (Pro, 9 €/month). Pricing work positioned NutsIQ against trackers plus
coaching tools at 15–30 €/month and asked for a ladder: a free entry point for acquisition, a main offer for
the €5–€20 MTT regular, and a premium offer for players who study weekly. The AI coach is the only feature
with a real marginal cost, so its use must be bounded per offer in a way players understand.

## Decision

**The grid**

| | Starter | Pro | Elite |
|---|---|---|---|
| Price | 0 € | 14,90 €/month · 149 €/year | 29,90 €/month · 299 €/year |
| History | 30 days | full | full |
| AI credits / month | 3 | 100 | 400 |
| Leaks | preflop view | preflop + AI explanations | + postflop leaks, breakdowns by phase/stack, all-in luck |
| Ranges | — | opening ranges | + push/fold equilibrium |
| Exercises | — | generic drills | + quiz on the player's own hands |
| Reports | — | weekly review once a month | weekly review every week + tournament debrief |

**Credits, not request counts.** One ledger already records every billed AI call (ADR-0008). Credits used
this month are computed from it (UTC month): explanation = 1, weekly review = 2, debrief = 5. No counter
column to keep in sync, nothing to reset. A call refused by the gate is not billed. Administrators are not
limited.

**Feature matrix in the domain.** `Feature` + `PlanFeatures.MinimumPlan` is the single source of truth;
the API refuses with `403 PLAN_REQUIRED {feature, plan}`; the web app shows locks (`app-plan-lock`) from
the same matrix, as a convenience only. Locked data is not sent: the leaks endpoint drops postflop leaks
below Elite and says how many were held back, so the page can show the upgrade argument without leaking
the content.

**Limits in configuration** (`Billing:Plans:{Starter,Pro,Elite}:{HistoryDays,MonthlyCredits}`), the matrix
in code. Changing a quota is an operations decision; moving a feature between offers changes the product
and deserves a release with tests.

**Tier from the Stripe price.** `subscriptions.tier` stores the offer read from the subscription's price
id (`Billing:Stripe:Prices:{Pro,Elite}:{Monthly,Yearly}`). An unknown price (old 9 € price, a manual price
in the dashboard) counts as Pro rather than locking a paying customer out. Order of the plan: administrator
→ billing off (both Elite) → the higher of the live subscription and an active grant → Starter.

**Changes of offer go through Stripe's portal.** Checkout is refused (`ALREADY_SUBSCRIBED`) while a
subscription is alive; the portal handles upgrades, downgrades and proration, and the webhook re-reads the
tier. A grant does not block checkout (a partner may subscribe before the access ends).

## Alternatives considered

- **Unlimited AI on Elite.** A single heavy user could cost more than the subscription; 400 credits is
  ~80 debriefs a month, far above observed study habits, while keeping the worst case bounded.
- **Separate quotas per AI feature.** Three meters for players to understand; one credit balance with
  visible costs is simpler and lets players spend where they need.
- **Storing the feature set per subscription.** Drifts from the price list; derivation keeps one truth.
- **Selling plan changes in-app.** Proration, taxes and invoices already live in Stripe's portal.

## Consequences and follow-ups

- Stripe: two products (Pro, Elite) with two prices each; portal configured to switch between the four
  prices; same webhook as ADR-0014. Env: `STRIPE_PRICE_PRO_MONTHLY`, `STRIPE_PRICE_PRO_YEARLY`,
  `STRIPE_PRICE_ELITE_MONTHLY`, `STRIPE_PRICE_ELITE_YEARLY`.
- The global AI budget (`Coaching:MonthlyBudgetUsd`, 20 $ by default, ADR-0008) must grow with paying
  subscribers, or a few Elite players exhaust it for everyone; measure the average cost per
  credit from the usage ledger before launch and adjust credit prices if a debrief costs more than 5×
  an explanation.
- Pro's weekly review is limited to one a month; "hand-by-hand gap" in ranges is available to every plan
  that has ranges — only push/fold is Elite-specific.
- The cash-game tracking and ICM rows of the pricing draft are not offered: the product does not support
  them yet, and the landing page does not promise them.
- Migration `PlanTiers`: `subscriptions.tier` (nullable), `pro_grants.plan` (default `Pro` for existing
  grants).
