# Poker Coach

Poker analytics and AI coaching platform. Initial target: Winamax No-Limit Hold'em MTT players.

Current phase: **Phase 0 — Foundation** (see [roadmap status](#status)).

## Repository layout

```
backend/                 .NET 10 modular monolith (ADR-0001)
  src/
    PokerCoach.Domain          business core, BCL only
    PokerCoach.Application     use cases
    PokerCoach.HandHistories   provider parsers (Winamax), Domain-only dependency
    PokerCoach.Infrastructure  EF Core / PostgreSQL, external systems
    PokerCoach.Api             ASP.NET Core host, composition root
  tests/
    PokerCoach.ArchitectureTests   dependency rule guards
    PokerCoach.Api.Tests           HTTP contract tests (no database)
    PokerCoach.Application.Tests   use-case tests (identity, import)
    PokerCoach.HandHistories.Tests parser golden tests on anonymized real files
    PokerCoach.IntegrationTests    PostgreSQL (Testcontainers, Docker required): import, constraints, races
web/                     Angular app (ADR-0003)
  src/app/core/          shell, i18n, theme, navigation
  src/app/shared/ui/     design-system primitives
  src/app/features/      one folder per product screen
  public/i18n/           fr / en / es translations
docs/adr/                architecture decision records
tools/                   anonymize_winamax.py (golden files)
docker-compose.yml       local PostgreSQL
```

## Prerequisites

- .NET SDK 10.0.1xx
- Node.js 24 (or ≥ 22.22.3) and npm 11
- Docker (local PostgreSQL)

## Getting started

```bash
# 1. Database
cp .env.example .env            # choose a local password
docker compose up -d

# 2. Google OAuth client (once) — Google Cloud Console > APIs & Services > Credentials
#    Create an OAuth client ID of type "Web application" with this authorized redirect URI:
#      http://localhost:4200/signin-google
#    (sign-in goes through the web dev server, which proxies /api and /signin-google to the API)

# 3. API (http://localhost:5080) — secrets never go in appsettings
cd backend
dotnet user-secrets set "ConnectionStrings:PokerCoach" \
  "Host=localhost;Port=<POSTGRES_PORT from .env>;Database=poker_coach;Username=poker_coach;Password=<your .env password>" \
  --project src/PokerCoach.Api
dotnet user-secrets set "Authentication:Google:ClientId" "<client id>" --project src/PokerCoach.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client secret>" --project src/PokerCoach.Api

dotnet tool install --global dotnet-ef   # once
dotnet ef database update --project src/PokerCoach.Infrastructure --startup-project src/PokerCoach.Api
dotnet run --project src/PokerCoach.Api
# Health: /health/live, /health/ready — OpenAPI (Development only): /openapi/v1.json

# 4. Web — open http://localhost:4200 (not the API port: the session cookie is same-origin)
cd web
npm ci
npm start
```

Database changes:

```bash
cd backend
dotnet ef migrations add <Name> --project src/PokerCoach.Infrastructure \
  --startup-project src/PokerCoach.Api --output-dir Persistence/Migrations
```

Checks run in CI:

```bash
cd backend && dotnet format PokerCoach.slnx --verify-no-changes && dotnet build && dotnet test
cd web && npx prettier --check "src/**/*.{ts,html,scss}" "public/i18n/*.json" && npm run build && npm test -- --watch=false
```

## Conventions

- **Errors**: every API error is RFC 9457 problem details with a stable `code`
  (`NOT_FOUND`, `VALIDATION_FAILED`, …) and a `traceId`. Clients branch on `code` only.
- **Secrets**: never in committed files. Local: `dotnet user-secrets` and `.env`.
- **Poker data**: never guessed. Missing information is `UNKNOWN`, not zero.
- **UI text**: always through translation keys; `fr`, `en` and `es` must have identical key sets
  (enforced by a unit test).
- **Colors**: only semantic tokens from `web/src/styles/_tokens.scss`.
- **Raw hand histories** are never committed; anonymized golden files are added explicitly.

## Status

Phase 0 — done:

- [x] ADR-0001 modular monolith, ADR-0002 MVP import by upload, ADR-0003 frontend foundations
- [x] Backend skeleton: layers, problem-details error contract, health checks, OpenAPI,
      JSON structured logs, fail-fast configuration, architecture and HTTP tests
- [x] Web skeleton: dark/light tokens, FR/EN/ES with runtime switching, responsive shell
      (sidebar / rail / mobile drawer), MVP-1 routes with empty states
- [x] Local PostgreSQL, CI workflow

Phase 0 — open:

- [x] First `dotnet build` of the skeleton.
- [x] `dotnet test` green, parser included.
- [x] Packages aligned on the latest 10.0.x patches.
- [ ] Upgrade web to Angular 22 (`ng update @angular/core@22 @angular/cli@22`): the scaffold was
      generated with Angular 21 because the authoring environment's Node version was too old for CLI 22.
- [x] Winamax format spike on two real tournaments (`docs/research/winamax-format-notes.md`)
- [x] Winamax hand-history and summary parsers with golden tests (ADR-0001 amendment 1);
      more samples still needed for re-entry, non-KO, Mystery KO, 9-max
- [x] Database naming: snake_case, applied by an in-house convention (`SnakeCaseNaming`), one
      schema per module.
- [ ] OpenTelemetry exporters and API Dockerfile once the hosting target is chosen
      (includes persisted data-protection keys, ADR-0004).
- [ ] ADR on opponent data (pseudonyms in hand histories): minimization and retention.

Phase 1 — Identity:

- [x] Google sign-in handled by the API, HttpOnly cookie session, CSRF token (ADR-0004)
- [x] Accounts with internal id + external identity, idempotent first sign-in
- [x] `GET /api/me`, `PUT /api/me/preferences` (language), sign-in page, sign-out, route guard
- [x] End-to-end Google sign-in verified locally (Google Cloud project `poker-coach-511022`, test mode)
- [x] First migration `InitialIdentity`
- [x] PostgreSQL integration tests (Testcontainers) for constraints and the sign-in race

Phase 2 — Import (MVP-1, ADR-0005):

- [x] Upload `.txt`/`.zip` (`POST /api/import/files`), batch status (`GET /api/import/batches/{id}`)
- [x] Background processing queue in PostgreSQL; idempotent files, accounts, tournaments and hands
- [x] Poker accounts detected from files: list, confirm, "not me" (`/api/poker-accounts`)
- [x] Migration `Import`
- [x] Web: import page (drag & drop of files, folders or zip; progress; account confirmation)
- [x] Re-entries (one tournament entry per summary block), late registration, bounty-only winnings

Phase 3 — Tournaments (MVP-1):

- [x] `GET /api/tournaments`: confirmed accounts only, period and buy-in filters, paging, totals
- [x] Result rule (domain `TournamentResult`): profit = prize + bounties − buy-in × entries; a summary
      without a "You won" line but with a finish position means zero; no summary means unknown, excluded
      from totals
- [x] Web: tournaments page (totals, filters, table, missing-summary marker)

Phase 4 — Performance (MVP-1):

- [x] `GET /api/performance`: totals, cumulative profit curve, breakdown by buy-in band, type, speed
- [x] ITM = share of entries that won a prize (bounties alone do not count: the files do not give the
      paid places)
- [x] Web: performance page (KPIs, accessible line chart with crosshair, breakdown tables)
- [x] Web: dashboard (last 30 days vs the 30 before, mini curve, latest tournaments, alerts for
      unconfirmed accounts and missing summaries)
- [x] Visual identity "Tapis de nuit" (felt, chip gold, glass panels, Sora + Plex, motion that
      respects reduced-motion)
