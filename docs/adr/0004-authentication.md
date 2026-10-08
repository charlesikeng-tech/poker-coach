# ADR-0004 — Authentication: Google sign-in handled by the backend, cookie session

- Status: Accepted
- Date: 2026-10-08

## Context

MVP-1 needs Google sign-in (spec §11–12). The web app and the API are served from the same origin
(dev server proxy in development, one host in production). The browser is the most exposed part of
the system; the future desktop agent will need its own credential later.

## Decision

**Backend-for-frontend.** ASP.NET Core runs the OpenID Connect flow with Google (Authorization
Code + PKCE, `Microsoft.AspNetCore.Authentication.Google`) and signs the user into an
**HttpOnly cookie** session. Angular never sees a token: it calls `/api/...` on the same origin.

- Session cookie `pc.session`: HttpOnly, SameSite=Lax, Secure outside Development, 14 days sliding.
  It holds one claim, our internal user id; no Google claim and no email.
- Google tokens are not stored (`SaveTokens = false`): we only need the identity, not Google APIs.
- Accounts: `identity.users` + `identity.external_identities`, unique on
  `(provider, provider_subject_id)`. The email is informational, never a key. A first sign-in is
  idempotent under concurrency thanks to that constraint (`ExternalSignInService`).
- **Secure by default:** a fallback authorization policy requires a signed-in user on every
  endpoint; health checks and the login endpoint opt out explicitly.
- API semantics: 401/403 problem details (`UNAUTHENTICATED`, `FORBIDDEN`), never a redirect.
- **CSRF:** SameSite=Lax plus a double-submit token: `GET /api/me` issues a readable `XSRF-TOKEN`
  cookie, Angular's HttpClient echoes it in `X-XSRF-TOKEN`, and an endpoint filter validates it on
  every unsafe request (`INVALID_ANTIFORGERY_TOKEN`).
- Open-redirect protection: the post-login `returnUrl` must be a local path (checked by the API and
  the web app).
- The language the visitor was using is carried through the Google round trip and becomes the
  account's preferred language on first sign-in.

## Alternatives considered

| Option | Why not |
|---|---|
| SPA gets Google/own JWT, stored in the browser | Tokens readable by any XSS; refresh-token handling in the browser; more code for less safety. |
| ASP.NET Core Identity (local accounts) | Not needed: Google only. Its tables and password features would be dead weight. |
| External identity platform (Auth0, Entra External ID…) | Cost and vendor dependency for one provider; can be reconsidered when several providers or B2B appear. |

## Consequences

- Same-origin deployment is required (reverse proxy or the API serving the web app).
- Data protection keys must be persisted and shared between instances in production, otherwise
  sessions break on restart or scale-out — to settle with the hosting ADR.
- The desktop agent (Phase 12) will need a separate credential (device authorization flow + API
  token scoped to sync). This ADR does not cover it.

## When to revisit

- A second identity provider, or account linking between providers.
- Cross-origin clients (mobile app, third-party integrations).
