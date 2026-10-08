# ADR-0001 — Modular monolith with layer projects and module namespaces

- Status: Accepted
- Date: 2026-10-07

## Context

Poker Coach starts as a single-team product. The domain is still being discovered (the Winamax
format has not been validated against real files yet). We need:

- one deployable unit, one database, one CI pipeline;
- the Domain layer kept free of ASP.NET Core, EF Core, provider SDKs and LLM SDKs;
- explicit business modules (Identity, Subscriptions, Import, Poker, Performance, Statistics,
  Coaching, Training, Progress) so that the code can be split later if it ever needs to be.

## Decision

One ASP.NET Core host and **four layer projects**:

```
PokerCoach.Domain          → no dependency except the BCL
PokerCoach.Application     → Domain
PokerCoach.Infrastructure  → Application, Domain, EF Core, Npgsql
PokerCoach.Api             → Application, Infrastructure (composition root)
```

Modules are **namespaces/folders inside each layer** (`PokerCoach.Domain.Poker`,
`PokerCoach.Application.Import`, …), not separate projects.

Rules:

1. Layer dependencies are enforced by the compiler (project references) and by an architecture
   test that inspects assembly references (`tests/PokerCoach.ArchitectureTests`).
2. A module never reaches into another module's internal types. Cross-module calls go through an
   Application-level service of the owning module. Until a namespace-level architecture test is
   added (NetArchTest or equivalent, once the package is vetted), this rule is enforced in review.
3. One `DbContext` for now, with one PostgreSQL schema per module (`identity`, `import`,
   `poker`, …) so ownership of tables stays visible and a future split stays possible.
4. No MediatR, no generic repository, no CQRS framework. Application services are plain classes
   injected into endpoints. Read endpoints may project directly from EF Core.

## Alternatives considered

| Option | Why not now |
|---|---|
| Project per module per layer (~40 projects) | Heavy ceremony for one team; slow builds; module boundaries are not yet known. |
| Project per module (each with Domain/Application/Infrastructure folders) | Better module isolation, but loses compiler enforcement of the Domain → EF Core rule inside each module. |
| Microservices | No scaling or team-topology need; would add distributed transactions to an import pipeline that needs strict idempotency. |

## Trade-offs

- Module isolation is weaker than layer isolation: it relies on namespaces, review and (later) a
  test. Accepted because the cost of a wrong module boundary today is higher than the cost of
  extracting a module project later.
- A single `DbContext` is simple but grows. Schemas per module keep it manageable.

## Risks

- Cross-module coupling creeping in unnoticed. Mitigation: namespace architecture test before
  Phase 4 (Poker domain), when the second and third modules appear.

## Amendment 1 — 2026-10-08: hand-history parsing library

A fifth project, `PokerCoach.HandHistories`, holds provider parsers (`IHandHistoryParser`,
`ITournamentSummaryParser`, Winamax implementation) and the provider-neutral parsed model.

- It references **Domain only** (enforced by an architecture test): no ASP.NET Core, no EF Core, no I/O.
- Why not Infrastructure: the future desktop agent must parse files locally (ADR-0002), and it must
  not drag EF Core and Npgsql with it.
- Why not Application: parsing a provider format is an adapter concern, not a use case; the import
  use case (Application) will consume `ParsedHand` and turn it into domain aggregates.
- Provider-specific code stays under its `Winamax` namespace; nothing outside the composition root
  and the import module refers to it.

## When to revisit

- More than one team working on the backend.
- A module needs a different scaling or deployment profile (e.g. AI analysis workers).
- The `DbContext` or a layer project becomes a merge-conflict hotspot.
