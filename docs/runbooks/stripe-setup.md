# Runbook — Stripe setup (test, then live)

What NutsIQ needs from Stripe (ADR-0014, ADR-0016). Do it once in **Test** mode, check the flow locally, then
redo it in **Live** mode at launch: test and live objects (prices, portal, webhook, keys) are separate.

## 1. Products and prices

Product catalogue → two products, two recurring prices each:

| Product | Monthly | Yearly |
|---|---|---|
| NutsIQ Pro | 14,90 € | 149 € |
| NutsIQ Elite | 29,90 € | 299 € |

Keep the four **price ids** (`price_…`): the app derives the offer from them.

## 2. Customer portal

Settings → Billing → Customer portal:

- payment method update, invoice history;
- cancellation **at the end of the period**;
- **Subscriptions → customers can switch plans**: add both products with their four prices; proration
  **with the difference invoiced immediately**; downgrades scheduled at the end of the period if offered.
  Without this, "Passer à Elite" falls back to the portal home page and no plan change is possible.

## 3. Webhook

Developers → Webhooks → add a destination on **Your account** (not connected accounts), snapshot events,
default API version (the app reads both the 2025 "basil" shape and older ones):

- URL: `https://app.nutsiq.com/api/billing/webhook`
- events: `checkout.session.completed`, `customer.subscription.created`, `customer.subscription.updated`,
  `customer.subscription.deleted`
- keep the signing secret (`whsec_…`).

## 4. API key

A **restricted key** for the app: Customers write, Checkout Sessions write, Customer portal write,
Subscriptions read. The full secret key is only for the Stripe CLI, if `stripe login` is not used.

## 5. Configuration

| Setting | Environment variable (docker-compose) |
|---|---|
| `Billing:Stripe:SecretKey` | `STRIPE_SECRET_KEY` |
| `Billing:Stripe:WebhookSecret` | `STRIPE_WEBHOOK_SECRET` |
| `Billing:Stripe:Prices:Pro:Monthly` / `Yearly` | `STRIPE_PRICE_PRO_MONTHLY` / `STRIPE_PRICE_PRO_YEARLY` |
| `Billing:Stripe:Prices:Elite:Monthly` / `Yearly` | `STRIPE_PRICE_ELITE_MONTHLY` / `STRIPE_PRICE_ELITE_YEARLY` |

Locally: `dotnet user-secrets set "<setting>" "<value>" --project src/PokerCoach.Api`. Without a secret key,
billing is off and every account has Elite.

## 6. Local test

```bash
stripe login                       # browser pairing, Test mode
stripe listen \
  --events checkout.session.completed,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted \
  --forward-to localhost:5080/api/billing/webhook
```

The CLI prints its own `whsec_…`: put it in `Billing:Stripe:WebhookSecret` and restart the API. Then, with an
account that is **not** an administrator (administrators always have Elite):

1. Subscription → Pro → card `4242 4242 4242 4242` → back on the app in Pro, CLI shows `[200]`.
2. "Passer à Elite" → Stripe's confirmation page with a prorated amount → Elite.
3. Cancel in the portal → still active until the end of the period.
4. Card `4000 0000 0000 0341` → renewal fails → "paiement en retard".

Troubleshooting: `[400]` = wrong `whsec`; CLI `403 Permission denied` = the CLI uses a key without rights
(`stripe logout --all && stripe login`); several subscriptions on one customer = cancel the extra ones
immediately in the dashboard (the app now re-reads Stripe before any checkout).

## 7. Before charging real money

- VAT: prices are tax-inclusive and Stripe Tax is off. Under the French VAT franchise, add "TVA non applicable,
  art. 293 B du CGI" to invoices; otherwise configure VAT (and OSS above 10 000 € of EU consumer sales). Check
  with an accountant.
- Terms of sale linked in Checkout; raise `Coaching:MonthlyBudgetUsd` with subscribers.
- Never paste keys in chats or tickets; rotate any key that was.
