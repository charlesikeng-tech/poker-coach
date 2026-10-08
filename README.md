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
    PokerCoach.HandHistories.Tests parser golden tests on anonymized real files
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

# 2. API (http://localhost:5080)
cd backend
dotnet user-secrets set "ConnectionStrings:PokerCoach" \
  "Host=localhost;Port=5432;Database=poker_coach;Username=poker_coach;Password=<your .env password>" \
  --project src/PokerCoach.Api
dotnet run --project src/PokerCoach.Api
# Health: /health/live, /health/ready — OpenAPI (Development only): /openapi/v1.json

# 3. Web (http://localhost:4200, /api proxied to the API)
cd web
npm ci
npm start
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
- [ ] `dotnet test` on the parser: written without access to the .NET SDK (rules validated on real
      files with an independent reference script); first run still to do. Align
      `Directory.Packages.props` on the latest 10.0.x packages.
- [ ] Upgrade web to Angular 22 (`ng update @angular/core@22 @angular/cli@22`): the scaffold was
      generated with Angular 21 because the authoring environment's Node version was too old for CLI 22.
- [x] Winamax format spike on two real tournaments (`docs/research/winamax-format-notes.md`)
- [x] Winamax hand-history and summary parsers with golden tests (ADR-0001 amendment 1);
      more samples still needed for re-entry, non-KO, Mystery KO, 9-max
- [ ] Decide database naming convention (snake_case via `EFCore.NamingConventions` or not)
      **before the first migration**.
- [ ] OpenTelemetry exporters and API Dockerfile once the hosting target is chosen.
- [ ] ADR on opponent data (pseudonyms in hand histories): minimization and retention.
