# ADR-0013 — Email and password accounts

Status: accepted (2026-10-10). Extends ADR-0004 (Google sign-in, cookie session), which stays as is.

## Context

Players without a Google account, or who prefer not to use it, need to sign up with an email and a
password. A password account is only safe if the address is proved and a forgotten password can be
reset: that means sending emails. Chosen with the product owner: password + email verification, emails
through **Brevo** (EU-hosted, consistent with the privacy page).

## Decision

**Same session as Google.** A successful password sign-in issues the same HttpOnly `pc.session` cookie,
holding only our user id. Nothing changes for the rest of the API.

**Model.** `identity.password_credentials` (one per user: normalized email — unique, enforced by the
database —, password hash, confirmation date, failed attempts, lockout end) and `identity.email_tokens`
(single-use links: only the SHA-256 of a 256-bit random token is stored; 24 h to confirm, 1 h to reset;
consumed by one atomic `UPDATE … WHERE used_at IS NULL`). Hashing: ASP.NET Core Identity's
`PasswordHasher` (PBKDF2-HMAC-SHA512, format v3, rehash on sign-in when parameters strengthen) without the
rest of Identity, whose stores, managers and tables would be dead weight.

**Flows** (`/api/auth`, anonymous, CSRF-checked, 10 requests a minute per client address):

| Endpoint | Behaviour |
|---|---|
| `POST /register` | 202 whatever happens. New email → account + confirmation link. Email of an existing account (Google or password) → an email "you already have an account" with a sign-in and a set-password link; nothing is attached. |
| `POST /email/confirm` `{token, password}` | Confirms **and requires the password**, then signs in. |
| `POST /password/login` | `INVALID_CREDENTIALS`; `EMAIL_NOT_CONFIRMED` only after a right password; `ACCOUNT_LOCKED` after 5 failures in a row (15 min). |
| `POST /email/resend`, `POST /password/forgot` | 202 whatever happens; at most 3 emails of a kind per account and hour. |
| `POST /password/reset` `{token, password}` | New password, address confirmed (the link proves the mailbox), other reset links voided, signed in. A Google account gains a password this way. |

**Security choices that are not obvious:**

- *No account enumeration.* Sign-up, resend and forgot answer the same; an unknown email at sign-in
  still costs one hash verification (timing).
- *Pre-hijacking.* Someone may sign up with another person's email. Their account never activates:
  confirming needs the password, which the mailbox owner does not have. The owner recovers the address
  through "forgot password". Signing up again before confirming replaces the pending password and voids
  older links.
- *Linking.* A first Google sign-in joins a password account with the same email **only if that
  address is confirmed** (both prove the same mailbox). A password is added to a Google account only
  through the reset link, never through sign-up.
- *Links are posted, not fetched.* The emailed link opens a page; the token is sent by POST with the
  password. Mail scanners that open links do not consume them.
- *Host header.* Links use `App:PublicUrl`, required outside Development (startup fails without it);
  building them from the request's Host would let anyone get real reset links pointing elsewhere.
- *CSRF.* `GET /api/me` now issues the anti-forgery token to anonymous visitors too, so the account
  forms are protected like every other unsafe request.
- *Passwords.* 10 to 128 characters, not the email, not one repeated character; no composition rules
  (NIST SP 800-63B).

**Email.** `IEmailSender` port; `BrevoEmailSender` (HTTP API, `Email:BrevoApiKey` from secrets,
`Email:FromAddress` a sender authenticated in Brevo with SPF/DKIM) when a key is configured. Without a
key, `LogEmailSender`: the email and its link go to the log in Development (to try sign-up locally),
elsewhere only a warning that nothing was sent — never the link. Plain transactional emails in the
account's language, no tracking.

## Alternatives considered

- **Magic link only.** No password stored, less code; not what was asked, and slower to sign in daily.
- **Password without email.** No recovery, anyone can claim any address. Refused.
- **ASP.NET Core Identity in full.** Brings `UserManager`, its own tables and token providers designed
  around its user entity; we need a fraction, on our existing `identity.users`.
- **External identity platform.** Cost and dependency for one more method (as in ADR-0004).

## Consequences and follow-ups

- Brevo: create the account, authenticate the sending domain (SPF, DKIM), set `Email:BrevoApiKey` and
  `Email:FromAddress`; the privacy page names Brevo as a processor.
- Sessions are not revoked when the password changes (cookie sessions, 14 days). A security stamp checked
  on each request would fix it; worth it once accounts hold paid plans.
- Accounts never confirmed stay in `identity.users`; a periodic cleanup (older than 7 days, no other
  identity) is to add.
- Looking up an account by email (`users.email`) is a scan: fine at this size, add a functional index on
  `lower(email)` if it shows in profiles.
