# ADR-0021 — Session serial: account-sharing prevention and session invalidation

- Status: Accepted
- Date: 2026-10-13
- Extends: ADR-0004 (cookie session), ADR-0013 (email and password accounts)

## Context

The product offers an Elite plan distributed to a fixed set of users. Two risks to address:

1. **Account sharing.** A user shares their credentials with a third party. Both use the account
   simultaneously. The existing stateless cookie session (ADR-0004) provides no mechanism to
   detect or prevent this.

2. **Session not revoked on password change.** Changing a password (via `POST /api/me/password`,
   added alongside this ADR) does not invalidate existing sessions. An attacker who obtained a
   valid cookie retains access until it expires (up to 14 days).

A stateless cookie session (ticket embedded in the cookie) cannot be invalidated server-side by
default. Switching to a server-side session store would fix both problems but adds infrastructure
complexity (session table, cache, or Redis) disproportionate to the current scale (< 100 users,
monolith, single instance).

## Decision

**Session serial on `identity.users`.** A `session_serial UUID` column is added to the `users`
table. It is rotated (new random UUID) on every sign-in, whether Google or email/password, and
on every password change.

The session cookie carries the serial as a `pc_ser` claim alongside `pc_uid`. On each request,
`CookieAuthenticationEvents.OnValidatePrincipal` checks the serial:

- If the claim is absent (cookie predates this feature): reject and sign out.
- If the claim does not match `users.session_serial` in DB: reject and sign out.
- To avoid a DB round-trip on every request: the check is throttled to once every 30 minutes per
  session, using a `pc_svt` (session validated timestamp) property stored in the cookie itself.
  `ShouldRenew = true` persists the updated timestamp.

**Consequence of a new sign-in:** the serial changes → any previously issued cookie becomes
invalid within 30 minutes of the next request made with it. A user sharing their credentials will
be signed out when the other party signs in.

**Consequence of a password change:** the serial rotates atomically with the hash update → all
other active sessions (different devices, a stolen cookie) are invalidated within 30 minutes.

**Deployment.** Existing cookies do not carry `pc_ser` and are rejected immediately on the first
request after deployment. All users are signed out once.

## Alternatives considered

| Option | Why not |
|---|---|
| Server-side session table (session id in cookie, serial in DB row) | Clean, no size increase in the cookie. Adds a DB read on every request regardless of throttle. Equivalent safety, higher infrastructure coupling. Worth it if we need multi-device management or explicit session listing. |
| Single active session (reject previous on new login) | Simpler guarantee (hard one-user rule). Bad UX: mobile and desktop cannot coexist. Not appropriate for a coaching tool used across devices. |
| IP / device fingerprinting | Brittle (VPNs, mobile networks), high false-positive rate, not reversible. |
| 2FA (TOTP or email OTP) | Would eliminate credential sharing entirely. Appropriate next step if data sensitivity increases or if sharing abuse is observed at scale. |

## Consequences

- `identity.users` gains `session_serial UUID NOT NULL` (migration `SessionSerial`).
- Cookie grows by one claim (< 40 bytes): no measurable impact.
- Every sign-in and password change triggers one additional `UPDATE identity.users SET session_serial = ...`,
  already part of the existing `SaveChangesAsync` call.
- The serial check adds at most one `SELECT` from `identity.users` per 30-minute window per session.
  At current scale this is negligible; revisit if high-frequency polling clients appear.
- The 30-minute validation window is a known gap: a stolen cookie stays valid for up to 30 minutes
  after the real user's next sign-in. Acceptable for the current threat model; reduce the interval
  or move to per-request validation if the threat model changes.

## When to revisit

- Explicit "sign out all devices" feature requested by users.
- Observable account-sharing abuse that the 30-minute window does not address fast enough.
- Addition of 2FA (would supersede account-sharing mitigation for credential theft).
