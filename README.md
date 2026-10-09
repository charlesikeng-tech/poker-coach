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
# Optional: the AI coach (ADR-0008). Without a key, explanations answer COACHING_UNAVAILABLE.
dotnet user-secrets set "Coaching:Anthropic:ApiKey" "<api key>" --project src/PokerCoach.Api

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
- [x] Tournament detail (`GET /api/tournaments/{id}`): stack curve, key moments ranked by share of
      stack (domain `KeyMoments`), session stats labelled as descriptive; master-detail page with
      name search
- [x] Hand replayer (`GET /api/hands/{id}`): table with seats by position (other players' pseudonyms
      never leave the server), street-by-street frames incl. all-in run-outs, previous/next hand and
      key moment; key moments and coach-cited hands link to it

Phase 4 — Performance (MVP-1):

- [x] `GET /api/performance`: totals, cumulative profit curve, breakdown by buy-in band, type, speed
- [x] ITM = share of entries that won a prize (bounties alone do not count: the files do not give the
      paid places)
- [x] Web: performance page (KPIs, accessible line chart with crosshair, breakdown tables)
- [x] Web: dashboard (last 30 days vs the 30 before, mini curve, latest tournaments, alerts for
      unconfirmed accounts and missing summaries)
- [x] Visual identity "Tapis de nuit" (felt, chip gold, glass panels, Sora + Plex, motion that
      respects reduced-motion)

MVP-2 — Game analysis (ADR-0006):

- [x] Domain `HandAnalyzer`: positions, VPIP, PFR, open, limp, steal, 3-bet, fold to 3-bet, flop
      c-bet, WTSD, W$SD, net chips / big blinds per hand
- [x] Versioned per-hand facts (`poker.hand_hero_facts`), computed in the background, SQL aggregates
- [x] `GET /api/statistics` (period, stack depth) and the statistics page (tiles, by position)
- [x] Migration `HandHeroFacts`
- [x] Hand coverage per tournament (levels, hand-number gaps, stack breaks, entries seen, start/end
      seen), computed in the background after facts, shown in the tournaments list
- [x] Migration `TournamentCoverage`
- [x] Rebuy/add-on tournaments (second Winamax summary layout), counted in money paid
- [x] Migration `Rebuys`
- [x] Statistics say what they rest on (hands, tournaments, complete histories) and can be
      restricted to complete histories
- [ ] Per-entry attribution of hands, postflop statistics

Ranges (ADR-0009):

- [x] Domain `HandClass` (169 starting hands, 13×13 grid)
- [x] `GET /api/ranges/opening`: actual opening range per position from the player's hands (RFI
      spots, per-hand counts, never extrapolated), against the position's reference rate
- [x] Web: Ranges page (position tabs, animated 13×13 grid with raise/limp shares and counts)
- [x] Reference opening ranges v1 per position and stack band (`ReferenceOpeningRanges`, range
      notation parser, seats named by distance to the button), compared hand by hand (gap view)
- [x] Table formats: statistics, leaks and ranges per format (6-max / full ring), positions named
      within the format, references by distance to the button
- [x] Training room (open or fold): spots dealt from the reference ranges (half near the range edge),
      missed hands come back, seats with an opening leak weighted up, answers stored
      (`training.opening_attempts`), progress per seat; shared `PokerTable` with the replayer
- [x] Migration `Training`
- [x] Push/fold equilibrium below 15 BB (`PushFoldNash`, preflop equity table from
      `backend/tools/preflop-equity`): push band in Ranges (stack 3–15 BB) and in the trainer
- [x] Migration `TrainingPushStack`

Bankroll (roadmap step 1):

- [x] Domain `BankrollLedger` (balance since a start date: known results + deposits, withdrawals,
      adjustments) and `RiskSimulation` (ROI with its 95 % interval, risk of ruin by bootstrap of the
      player's own results, 2,000 paths, seeded so figures do not move between refreshes)
- [x] `GET /api/bankroll`, `PUT /api/bankroll/settings`, `POST|DELETE /api/bankroll/movements`
      (schema `bankroll`)
- [x] Web: Bankroll page (onboarding, balance curve with movements, buy-in limit of the rule,
      true-ROI interval, risk of ruin, movements); replaces the empty Sessions page (`/sessions`
      redirects)
- [x] Migration `Bankroll`

All-in EV (roadmap step 2):

- [x] Domain `HandEvaluator` (7 cards, bit arithmetic) and `ShowdownEquity` (exact: every board,
      side pots, splits)
- [x] `AllInExpectation`: preflop all-ins with every hand shown, unmatched shove given back,
      side pots; stored in the hero's facts (facts version 2: everything is recomputed once)
- [x] `GET /api/statistics/all-in` and the "All-in luck" panel in Statistics (actual vs expected
      in BB, verdict, curve with the expected line, biggest swings linking to the replayer)
- [x] Migration `AllInEv`
- [x] Big blind defence in the trainer: a seat shoves (below 15 BB), call or fold from the big blind,
      checked against the push/fold equilibrium's call ranges; attempts stored with the shover,
      progress per shover
- [x] Migration `TrainingDefence`

Postflop (roadmap step 3):

- [x] Facts version 3: fold to / raise the flop c-bet, turn c-bet, flop check-raise, won when saw flop,
      postflop aggression frequency (`PostflopPlay`)
- [x] Statistics: preflop and postflop sections; leaks: references version 2 with five postflop
      statistics (ADR-0007 amendment), example hands for the coach
- [x] Migration `PostflopStats`

Weekly plan (roadmap step 4, ADR-0010):

- [x] Domain `WeeklyPlanner` (priorities from the leaks of the last 90 days, week assessment, drills)
- [x] `GET /api/progress` (builds the week's plan on first read), `POST /api/progress/plan/rebuild`;
      schema `progress`
- [x] Web: Plan page (priorities with baseline / week / target, drill and coach links, drill goal,
      previous weeks); training room deep links (`?mode=&seats=&format=`)
- [x] Migration `WeeklyPlan`

Data rights (before opening to other players):

- [x] `GET /api/me/export`: zip with the uploads as sent, tournaments.csv, account.json
- [x] `DELETE /api/me` (confirmation word, anti-forgery): cascade delete of everything the user owns,
      AI usage rows kept without the user for the spending cap
- [x] Web: My account page (link on the name in the top bar)

Tournament phases and trends:

- [x] `TournamentPhase` by blind level (early 1–6, middle 7–12, late 13+: hand histories do not give
      the players left, so no guessed bubble / money / final table); `phase` filter on statistics
- [x] `GET /api/statistics/breakdowns`: statistics by phase and by month, with the overall reference
      ranges; web: phase table and month-by-month chart with the reference band

Quiz on real hands:

- [x] `RealSpots`: the hero's real raise-first-in spots (last 90 days), judged by the drills' answer key
      (references above 15 BB, push/fold equilibrium below); missed ones are asked again until answered
      right (`training.opening_attempts.source_hand_id`)
- [x] `GET /api/training/opening/spot?mode=real` (404 `NO_REAL_SPOT_LEFT`), answers with `sourceHandId`;
      web: "My hands" mode with the date, what the hero did that day and a link to the replay
- [x] Migration `TrainingRealHands`

MVP-3 — Leaks (ADR-0007):

- [x] Domain `LeakDetector`: reference ranges v1 (low-stakes MTT, 15+ BB), Wilson 95 % guard,
      confirmed / to watch / not judgeable yet
- [x] `GET /api/leaks` and the leaks page (range gauge, why it costs, what to work on)
- [x] AI explanations with example hands (ADR-0008): `POST /api/leaks/explanations`, Anthropic
      adapter with structured output, anonymised hand stories, stored by fingerprint, per-user daily
      limit and global monthly budget, usage ledger
- [x] Migration `Coaching`
- [ ] Adjustable references; evaluation set for explanations
