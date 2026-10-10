# ADR-0011 — Deployment readiness

Status: accepted (2026-10-10). The hosting provider is decided separately (roadmap step 5).

## Context

Poker Coach is about to be opened to a few test players. Whatever the host (managed containers or a
VPS), the app must survive restarts and redeploys, run behind a TLS-terminating proxy, change its schema
safely, and say what it does with personal data. Constraint met along the way: no new NuGet package
(none was needed).

## Decision

**One container, one origin.** The API serves the compiled Angular app from `wwwroot`. Same origin
keeps the cookie session, the anti-forgery tokens and the CSP simple (no CORS, no third-party cookie,
one deployable). Client-side routes answer `index.html`; `/api/*`, `/health/*` and file-like paths
never do, so an unknown API route stays a 404 problem response and a missing bundle is not answered
with HTML. Hashed bundles are cached for a year (`immutable`), everything else is revalidated
(`no-cache`), so a deploy is picked up on the next navigation.

Image: multi-stage (Node build, .NET publish), `aspnet:10.0-noble-chiseled` runtime: no shell, no
package manager, non-root. Invariant globalization was already the rule, so no ICU is needed. Probes are
HTTP: `/health/live` (process only, so a database outage never causes a restart loop) and
`/health/ready` (database).

**Data-protection keys in PostgreSQL** (`platform.data_protection_keys`). They sign the session cookie
and the anti-forgery tokens; the default store is the container's file system, lost on every restart,
which would sign every player out and break forms after each deploy. A hand-written `IXmlRepository`
(a few lines, on our DbContext and naming) instead of the EF Core package. Keys are stored unencrypted:
whoever can read that table can read all user data anyway. To revisit with a key vault once one exists
(`ProtectKeysWith…`).

**Behind a proxy:** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (set in the image) trusts
`X-Forwarded-Proto`/`For`, so the Google redirect URI is `https://…` and Secure cookies are issued. Safe
only because the container is reachable through the platform's proxy alone: the port must never be
published directly. HSTS (one year) outside Development.

**Security headers on every response:** CSP `script-src 'self'` (no inline script; Angular's critical
CSS inlining is disabled because it injects an `onload` handler), `style-src 'self' 'unsafe-inline'`
(Angular injects component styles at runtime; a nonce would mean rendering `index.html` per request),
`frame-ancestors 'none'`, `nosniff`, `Referrer-Policy`, `Permissions-Policy`, COOP. Checked in Chromium
against the production build: no violation.

**Migrations are a release step, not a startup step.** `dotnet PokerCoach.Api.dll migrate` (same image)
applies pending migrations and exits. Run by the pipeline before the new version takes traffic.
Migrating at startup would let two instances race and turn a failed migration into a crash loop. No
`efbundle`: one artifact, no EF tool version to keep in step. Migrations must stay backward compatible
with the running version (expand, then contract), since the old version serves while the new schema
lands.

**First-party usage measure** (`platform.feature_usage`): one row per user, feature (first path segment
after `/api/`) and day, written once per day per instance (deduplicated in memory), after the response.
Enough to see what testers use and whether they come back (daily/weekly actives, retention by cohort),
without a third-party tracker, a cookie or a banner. Rows go with the account (FK cascade) and are
purged after 13 months. A failed write is logged and never fails the request. Read with SQL for now:

```sql
-- Weekly active users and the features they open
SELECT date_trunc('week', day) AS week, feature, count(DISTINCT user_id) AS users
FROM platform.feature_usage GROUP BY 1, 2 ORDER BY 1 DESC, 3 DESC;
```

**Privacy page** (`/privacy`, public, FR/EN/ES, linked from sign-in and account): data kept, purposes,
the AI provider (anonymised hand stories, processed in the US), essential cookies only, EU hosting,
retention (account lifetime; usage 13 months; backups gone within 30 days of a deletion), rights (export
and deletion are self-service). The contact address (`PRIVACY_CONTACT_EMAIL`) must be set before
inviting players outside the team. Hosting must therefore be in the EU and database backups kept 30 days
at most.

**CI:** the image is built on every run, smoke-tested against PostgreSQL (migrate, ready, SPA served,
API protected) and pushed to GHCR (`:sha`, `:main`) from `main`. Deploying a given SHA is then the
host's job, decided with the provider.

## Alternatives considered

- **Front on a static host or CDN, API elsewhere.** Better edge caching, but two origins: CORS, a
  cross-site cookie (or a token in the browser, which ADR-0004 rules out), two deploys. Not worth it at
  this scale.
- **Migrate at startup.** Simplest, but races between instances and crash loops on failure.
- **Third-party analytics (Plausible, GA…).** More dashboards, but a processor, a script in the CSP and,
  for GA, a consent banner. The question today is small: which features are used, by how many testers.
- **OpenTelemetry now.** Deferred: structured JSON logs to stdout plus the platform's HTTP metrics cover
  a test phase. The exporter goes in with the hosting decision (Azure Monitor or OTLP), without touching
  the business code.

## Consequences

- Release order: build image → `migrate` → roll out. Schema changes must work with the previous version.
- The proxy, not the container, is the public entry point (forwarded headers are trusted).
- Before inviting outside testers: set the privacy contact, host in the EU, cap backup retention at 30
  days, register the production redirect URI with Google.
