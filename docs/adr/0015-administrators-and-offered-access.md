# ADR-0015 — Administrators and offered Pro access

Status: accepted (2026-10-11). Extends ADR-0014 (subscriptions).

## Context

Once billing is on, every account without a paid subscription is Free, the product owner's included. The
landing page also promises Pro to partner coaches and streamers during the test phase, and support will
sometimes want to give a month as a gesture. Asking any of them to enter a card for a 100 % coupon is a
poor first contact and counts them as 0 € subscribers in Stripe's revenue figures.

Billing operations themselves (refunds, cancellations, coupons, failed payments, invoices) are already
covered by the Stripe dashboard; rebuilding them in the app would create a second source of truth.

## Decision

**Administrators are configured, not stored.** `Admin:Emails` lists the addresses of administrators. An
account whose email is listed is an administrator and always has Pro. No role table, no screen to manage
roles: one or two people administer the product today, and a configuration change is the right amount of
ceremony for granting that power.

An email only reaches an account once proved, so listing an address cannot be taken over by someone who
signs up with it:

- an email and password account cannot sign in before confirming its address (ADR-0013);
- a Google sign-in now keeps the email only when Google says it is verified (`email_verified`). Before
  this, an unverified Google email was stored and could also have joined a confirmed password account
  with the same address (ADR-0013 linking). Accounts created before the change keep the email they have.

**Offered access.** `subscriptions.pro_grants`, one row per user (cascade with the user): reason, optional
end, author, date. Granting again replaces the row, revoking deletes it. The author is kept for the record
without a foreign key, so deleting an administrator's account keeps the grants they made.

**The plan, in order**: administrator → billing off → live Stripe subscription → active grant → Free.
`GET /api/billing` returns the source (`admin`, `billingOff`, `subscription`, `grant`, `free`) and the
grant's end, so the subscription page can say "Pro offered until 31 December". A paid subscription comes
before a grant, and a grant does not block checkout: a partner can subscribe before the access ends.

**Administration page** (`/admin`, shown when `GET /api/me` says `isAdmin`):

- `PUT /api/admin/grants {email, reason, endsAt?}` — the account must exist (`USER_NOT_FOUND`); the end, if
  given, is in the future;
- `DELETE /api/admin/grants/{userId}`;
- `GET /api/admin` — grants (ending soonest first) and subscribers (accounts with a Stripe status, at most
  500), each subscriber linked to its customer page in the Stripe dashboard.

Every admin route checks the administrator status on the server (`ADMIN_REQUIRED`, 403); the web app's
guard is convenience only. Writes are CSRF-checked like every other unsafe request.

## Alternatives considered

- **Stripe coupon at 100 %.** No code, but a card is required at checkout, the account shows as a 0 €
  subscriber, and expiry is per coupon rather than per person.
- **Role column on users and an admin UI to manage roles.** Useful with a support team; today it adds a
  privilege-escalation surface (who may make whom admin) for one person.
- **Admin by user id in configuration.** Immune to email changes, but the owner does not know their id
  and ids differ between environments; emails are proved, so they are safe enough.
- **Admin back office for billing.** Stripe's dashboard does it better; see the ADR-0014 rationale.

## Consequences

- Set `Admin:Emails` (for example `Admin__Emails__0=you@nutsiq.com`) wherever the app runs.
- Grants are visible to administrators only; a granted user sees "Pro offered" on the subscription page.
- Follow-ups: an email to the user when access is offered or about to end; promotion codes for the
  partners' communities are created in Stripe (Checkout already accepts them).
