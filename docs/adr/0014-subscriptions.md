# ADR-0014 — Subscriptions: Free and Pro plans, paid through Stripe

Status: accepted (2026-10-10).

## Context

The product is now NutsIQ (nutsiq.com) and it needs revenue that covers the AI coach and hosting. The
product owner validated the offer:

| Plan | Price | Content |
|---|---|---|
| Free | 0 € | Import without limit; statistics, leaks, ranges and tournaments over the **last 30 days**; **3 new leak explanations a month** |
| Pro | **9 €/month or 79 €/year** | Full history, tournament debrief, weekly review, the daily coaching limits of ADR-0008/0012 |
| Coach | later | Student access, referral codes — not built now |

At 9 € a Pro subscriber pays less than one buy-in of the target player (5–20 € MTTs). The monthly AI budget
cap (ADR-0008) stays as the global safety net.

## Decision

**Stripe, hosted pages only.** Stripe Checkout takes the payment and Stripe's customer portal handles
card changes, invoices, plan switches (monthly ↔ yearly) and cancellation. We never see a card number,
we do not build invoice or VAT screens, and SCA/3-D Secure is Stripe's job. Taxes: Stripe Tax can be turned
on in the dashboard without code (needed once sales are above the EU one-stop-shop threshold).

**Plain HTTP, no SDK.** We call five Stripe endpoints (`customers`, `checkout/sessions`,
`billing_portal/sessions`, `subscriptions` list, `customers` delete) with form-encoded requests and verify
webhook signatures ourselves (HMAC-SHA256 of `timestamp.payload` with the endpoint secret, constant-time
comparison, 5-minute tolerance). Same choice as Brevo (ADR-0013): about 200 lines we own instead of a
large SDK whose release train we would follow; the package feed of the build environment cannot reach
nuget.org either. If we later need many more Stripe objects, switching to Stripe.net is local to the
adapter.

**Model.** `subscriptions.subscriptions`, one row per user (key `user_id`, cascade with the user):
Stripe customer id (unique), subscription id, status as Stripe names it, interval, end of the current
period, cancel-at-period-end, last sync time. The **plan is derived**, never stored: Pro while the
status is `active`, `trialing` or `past_due` (Stripe is still retrying the card; cutting a paying user on
the first failed charge would be hostile), Free otherwise.

**Webhooks: state sync, idempotent by construction.** `POST /api/billing/webhook` (anonymous, signature
required, no CSRF) accepts any event carrying a customer, then **re-reads that customer's subscriptions
from Stripe** and upserts the row. We never apply the event payload itself, so:

- duplicates (Stripe delivers at least once) rewrite the same state;
- out-of-order delivery cannot roll the state back: whatever event arrives last, the row reflects Stripe
  now;
- a missed event is repaired by the next one, or by opening the subscription page (it syncs on read when
  the last sync is older than a day).

No event table: deduplication would add a write per event and protect nothing the re-read does not.
Unknown customers are acknowledged (200) and logged; a Stripe outage during the re-read answers 500 so
Stripe retries.

**Checkout.** `POST /api/billing/checkout {interval}` creates the Stripe customer on first use (metadata
`user_id`; the unique `user_id` row makes concurrent clicks converge on one customer, an orphan Stripe
customer at worst), then a Checkout session in `subscription` mode with promotion codes allowed, and
returns its URL. Success and cancel URLs come from `App:PublicUrl`, never from the request (same reason as
ADR-0013). An active Pro gets `ALREADY_SUBSCRIBED` and is sent to the portal instead: no double
subscription. `POST /api/billing/portal` returns a portal URL.

**Entitlements are an Application concern.** `PlanAccess` answers "which plan" and "from when may this
user see history". It is applied:

- to the history window of `/api/statistics*`, `/api/leaks`, `/api/ranges/opening`, `/api/tournaments`
  (list) and `/api/performance`: a Free user's `from` is raised to 30 days ago. Data is **never deleted**:
  imports stay unlimited and upgrading shows the whole history at once — the best upgrade argument we
  have. Tournament and hand detail stay reachable (only the lists are windowed), and the bankroll, the
  weekly plan and the data export are not windowed (money records and GDPR access are not features to
  sell);
- in `CoachingGate`: debrief and weekly review answer `PLAN_REQUIRED` (403) for Free; leak explanations
  are counted per calendar month and answer `PLAN_LIMIT_REACHED` (429) after 3. Stored
  texts stay readable for free, as before.

`GET /api/billing` returns the plan, status, renewal or end date, the Free limits and what is used this
month, and whether billing is enabled; the web app reads it to show limits and upgrade prompts.

**Billing disabled = everyone Pro.** Without `Billing:Stripe:SecretKey`, billing endpoints answer
`BILLING_UNAVAILABLE` (503) and every account has Pro. Development, tests, self-hosting and today's
testers keep everything until we switch billing on; turning it on is configuration only (secret key,
webhook secret, two price ids).

**Account deletion closes the Stripe customer first.** Deleting a Stripe customer cancels its
subscriptions immediately (no further charge) and removes the customer; Stripe keeps the invoices it is
legally required to keep. If Stripe cannot be reached the deletion is refused (502) rather than leaving a
paying subscription without an account.

## Alternatives considered

- **Paddle / Lemon Squeezy (merchant of record).** They handle EU VAT as the seller, at about 5 % + fees
  versus Stripe's ~1.5 % + 0.25 € for EU cards. Worth reconsidering if VAT handling becomes a burden; the
  adapter boundary keeps it a local change.
- **Applying webhook payloads with an event-ordering column.** More code and still wrong when an event is
  missed. Re-reading is one extra API call per event, well within Stripe's limits at our volume.
- **Storing the plan.** It would drift from Stripe's status; deriving it keeps one source of truth.
- **Hiding old data by deleting it for Free users.** Destroys the upgrade argument and user trust.
- **Hard paywall (no Free plan).** The free plan is the acquisition channel for coaches and streamers
  (brand kit); its cost is bounded (3 explanations a month).

## Consequences and follow-ups

- To switch on: create the Pro product with two prices (9 €/month, 79 €/year) in Stripe, configure the
  customer portal (cancel at period end, switch between the two prices, invoice history), add a webhook
  endpoint `https://app.nutsiq.com/api/billing/webhook` for `checkout.session.completed` and
  `customer.subscription.*`, then set `Billing:Stripe:SecretKey`, `Billing:Stripe:WebhookSecret`,
  `Billing:Stripe:Prices:Monthly`, `Billing:Stripe:Prices:Yearly`.
- Legal (France, consumer sales): CGV with the 14-day withdrawal right and its waiver when the user asks
  to start the service immediately, legal notice, terms link in Checkout (Stripe setting). To be written
  or validated by a lawyer; the landing page links to them.
- The Coach plan (student access, referral codes) gets its own ADR when a first coach signs up.
- Free-plan limits live in configuration (`Billing:Free:HistoryDays`, `Billing:Free:MonthlyExplanations`)
  so pricing tests do not need a release.
