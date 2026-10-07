# ADR-0003 — Web frontend foundations

- Status: Accepted
- Date: 2026-10-07

## Context

The Angular app is the primary product surface. It must be premium-looking, dark-first, FR/EN/ES,
accessible (WCAG 2.2 AA target) and chart-heavy, while staying simple for a small team.

## Decisions

| Concern | Decision | Main reason |
|---|---|---|
| Component model | Standalone components, **zoneless** change detection, signals for local and shared UI state | Angular defaults; less magic, better performance. |
| Global state | **None** (no NgRx/Akita). Feature-scoped services exposing signals. | No cross-feature complexity yet. Revisit if several features share mutable server state. |
| i18n | **Transloco**, runtime language switch, JSON files in `public/i18n/` | The top bar has a live language selector; Angular's built-in i18n is build-time (one bundle per locale, reload to switch). |
| Language resolution | user preference (API, Phase 1) → stored choice → browser → `en` | Spec §13. Pure function, unit-tested. |
| Design system | Own primitives in `shared/ui/`, **semantic CSS custom properties** in `src/styles/_tokens.scss`, no component library styling | Full control of the visual identity; no fighting Material/PrimeNG themes. |
| Behaviour primitives | **Angular CDK** (overlay, a11y, listbox) added when the first overlay component is built | Focus trap, keyboard navigation and ARIA for dialogs/selects are expensive to get right by hand. |
| Charts | **Apache ECharts** (canvas), added in Phase 5 | Covers line/area/stacked/heatmap/calendar with large series; themable from our tokens. Chart.js is weak on heatmaps; D3 is too costly to maintain. |
| Icons | `lucide` core package (ISC) rendered by a tiny `app-icon` component | The Angular wrapper caps its peer range at Angular 21, which would block upgrades. |
| Typography | IBM Plex Sans (variable, self-hosted via `@fontsource-variable`), `tabular-nums` for all metrics | Engineered, precise character that fits an analytics tool and is less ubiquitous than Inter. Self-hosted: no third-party font requests (privacy, CSP). Tabular figures keep tables and KPI cards aligned. |
| Theme | Dark by default, light supported, `data-theme` on `<html>`, choice persisted locally | Dark is the primary experience (spec §56), not "follow OS". |
| Period filter (Phase 5) | Single source of truth in the **URL query string** (`?period=30d`), read through one service | Shareable, survives reload and back button; avoids top-bar vs page selector conflicts. |
| API types | Hand-written services; TypeScript types **generated from OpenAPI** (types only) once the first endpoints exist | Keeps contracts honest without adopting a heavy generated client. |
| Dev backend access | Angular dev-server proxy for `/api` | No CORS configuration needed in development. |
| Tests | Vitest (Angular default). Test logic and critical flows, not "component creates". | Spec §83. |

## Navigation scope

Only screens that exist are shown. MVP-1 navigation: Dashboard, Performance, Tournaments,
Sessions, Statistics, Import. Hands, Leaks, Coach, Training and Progress appear when their
phase ships — no placeholder screens for features that do not exist.

## Risks

- Transloco keys are strings: typos show up at runtime. Mitigation: missing-key handler logs in
  dev; a key-consistency check across `fr/en/es` runs in tests.
- Own design system = ongoing cost. Mitigation: build primitives only when a screen needs them.

## When to revisit

- Shared server state across many features (consider a store or a query cache library).
- Need for SSR/SEO pages (marketing site is out of scope of this app).
