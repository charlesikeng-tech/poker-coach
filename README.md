# NutsIQ

**Poker analytics and AI coaching platform for tournament players.**

Import your hand histories, get detailed statistics, identify leaks, train with personalized drills, and receive AI-powered coaching — all in one place.

> Product name **NutsIQ** ([nutsiq.com](https://nutsiq.com)). The codebase keeps its original name `PokerCoach`.

## Features

- **Hand history import** — drag & drop `.txt` or `.zip` files (Winamax MTT, Betclic cash game); background processing with idempotent deduplication
- **Tournament tracking** — results, profit, buy-in filters, stack curves, key moments
- **Hand replayer** — street-by-street replay with positions, all-in run-outs, and navigation
- **Statistics** — VPIP, PFR, 3-bet, c-bet, WTSD and more, by position, phase, and month; all-in expected value with equity computation
- **Leak detection** — reference-based analysis with AI explanations citing your actual hands
- **Ranges** — opening, defence, vs 3-bet grids (169 hands) with reference comparison; push/fold equilibrium
- **Training room** — drills on openings, defence, and sizing; spots from references and from your own history
- **AI coach** — tournament debriefs, weekly reviews, personalized weekly plan with drill goals
- **Bankroll management** — balance tracking, risk of ruin simulation, ROI confidence intervals
- **Subscriptions** — Starter / Pro / Elite tiers via Stripe; AI credit system with usage guard

## Tech stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, ASP.NET Core, EF Core, PostgreSQL 18 |
| Frontend | Angular 21, TypeScript 5.9, SCSS |
| AI | Claude (Anthropic) with structured output |
| Infra | Docker, Caddy, Scaleway, GitHub Actions CI/CD |
| Monitoring | Sentry (frontend + backend) |

## Repository layout

```
backend/                 .NET 10 modular monolith
  src/
    PokerCoach.Domain          business core, BCL only
    PokerCoach.Application     use cases
    PokerCoach.HandHistories   provider parsers (Winamax, Betclic)
    PokerCoach.Infrastructure  EF Core / PostgreSQL, external systems
    PokerCoach.Api             ASP.NET Core host, composition root
  tests/
    PokerCoach.ArchitectureTests   dependency rule guards
    PokerCoach.Api.Tests           HTTP contract tests
    PokerCoach.Application.Tests   use-case tests
    PokerCoach.HandHistories.Tests parser golden tests
    PokerCoach.IntegrationTests    PostgreSQL via Testcontainers
web/                     Angular app
  src/app/core/          shell, i18n, theme, navigation
  src/app/shared/ui/     design-system primitives
  src/app/features/      one folder per product screen
  public/i18n/           fr / en / es translations
site/                    landing page (static, self-hosted fonts)
deploy/                  Caddyfile, Dockerfile.edge, scaleway/
docs/adr/                architecture decision records (22 ADRs)
docs/runbooks/           dns-setup, scaleway-deploy, stripe-setup
tools/                   anonymize_winamax.py, dev-setup.command
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

# 3. API (http://localhost:5080) — secrets never go in appsettings
cd backend
dotnet user-secrets set "ConnectionStrings:PokerCoach" \
  "Host=localhost;Port=<POSTGRES_PORT from .env>;Database=poker_coach;Username=poker_coach;Password=<your .env password>" \
  --project src/PokerCoach.Api
dotnet user-secrets set "Authentication:Google:ClientId" "<client id>" --project src/PokerCoach.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client secret>" --project src/PokerCoach.Api
# Optional: AI coach (without a key, coaching features return COACHING_UNAVAILABLE)
dotnet user-secrets set "Coaching:Anthropic:ApiKey" "<api key>" --project src/PokerCoach.Api

dotnet tool install --global dotnet-ef   # once
dotnet ef database update --project src/PokerCoach.Infrastructure --startup-project src/PokerCoach.Api
dotnet run --project src/PokerCoach.Api
# Health: /health/live, /health/ready — OpenAPI (dev only): /openapi/v1.json

# 4. Web — open http://localhost:4200 (not the API port: the session cookie is same-origin)
cd web
npm ci
npm start
```

### Database migrations

```bash
cd backend
dotnet ef migrations add <Name> --project src/PokerCoach.Infrastructure \
  --startup-project src/PokerCoach.Api --output-dir Persistence/Migrations
```

### CI checks (run locally)

```bash
cd backend && dotnet format PokerCoach.slnx --verify-no-changes && dotnet build && dotnet test
cd web && npx prettier --check "src/**/*.{ts,html,scss}" "public/i18n/*.json" && npm run build && npm test -- --watch=false
```

### Production image

```bash
docker compose --profile app up --build   # PostgreSQL + migrate + app on http://localhost:8080
```

Release order: build → `docker run <image> migrate` → deploy. Probes: `/health/live`, `/health/ready`.

Production runs on Scaleway — see `deploy/scaleway/` and [`docs/runbooks/scaleway-deploy.md`](docs/runbooks/scaleway-deploy.md).

## Conventions

| Convention | Rule |
|-----------|------|
| Errors | RFC 9457 problem details with a stable `code` and `traceId`; clients branch on `code` only |
| Secrets | Never in committed files; local: `dotnet user-secrets` and `.env` |
| Poker data | Never guessed; missing information is `UNKNOWN`, not zero |
| UI text | Translation keys only; `fr`, `en`, `es` must have identical key sets (enforced by test) |
| Colors | Semantic tokens from `web/src/styles/_tokens.scss` only |
| Hand histories | Raw files never committed; anonymized golden files added explicitly |

## Architecture decisions

22 ADRs document every significant technical choice — see [`docs/adr/`](docs/adr/).

## Roadmap

See [`docs/ROADMAP.md`](docs/ROADMAP.md) for the detailed phase-by-phase status.
