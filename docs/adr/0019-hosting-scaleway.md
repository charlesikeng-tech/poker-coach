# ADR-0019 — Hosting: one Scaleway instance, managed PostgreSQL

Status: accepted (2026-10-12). Completes ADR-0011 (deployment readiness), which left the provider open.

## Context

ADR-0011 made the app deployable anywhere: one container serving API and web app, migrations as a release
step, data-protection keys in the database, health probes, an image per commit on GHCR. What it required of
the host: **EU hosting**, **database backups kept 30 days at most** (the privacy page promises that a deleted
account is gone from backups within 30 days), TLS in front of a container that is never published directly.

Two constraints from the code narrow the choice:

- **The import worker runs in the app process** (ADR-0005), as does the hand-facts recomputation after a
  facts-version bump. A platform that scales to zero or freezes idle containers would stall imports. The app
  must stay always-on.
- **One instance only.** The AI guard serialises on a PostgreSQL advisory lock (ADR-0017), so it would be
  correct with several instances, but the background workers (imports, facts recomputation) have never been
  run with two. Nothing today needs two.

The landing page (`site/`) is static and lives on the apex domain; the app on `app.`.

Options compared: Scaleway (instance or Serverless Containers), Clever Cloud, Railway, Fly.io, Render, a bare
VPS (Hetzner, OVH). Railway, Fly.io and Render run in the EU on request but are US companies with US-hosted
control planes and billing; Clever Cloud is French and fully managed but costs about twice as much for the
same size; Serverless Containers scale to zero, which breaks the in-process worker unless pinned to one warm
instance at a price close to the instance's.

## Decision

**Scaleway, region fr-par.**

- **Compute: one instance** (PLAY2-PICO, 1 vCPU / 2 GB, Ubuntu LTS) running Docker Compose
  (`deploy/scaleway/compose.yml`):
  - `edge` — Caddy, ports 80/443: automatic Let's Encrypt certificates for the app and the landing page, HTTP→HTTPS,
    the landing page as static files, the app behind `reverse_proxy`;
  - `app` — the CI image, `expose`d only on the Compose network, so the forwarded headers it trusts can only come
    from Caddy (ADR-0011);
  - `migrate` — same image, `migrate` command, run by `deploy.sh` before the new version starts.
- **Database: Scaleway Managed PostgreSQL** (DB-DEV-S), attached to a **private network** with the instance and no
  public endpoint; TLS required (`SSL Mode=Require`). Automatic backups with a **retention of 7 days** (≤ 30, as
  promised), managed patching. The database is the only stateful piece; the instance can be rebuilt from this
  repository and two env files.
- **Edge image** (`deploy/Dockerfile.edge`): Caddy with the Caddyfile and `site/` baked in, built and pushed by CI
  next to the app image (`ghcr.io/<repo>-edge`, same `:sha` / `:main` tags). A release is one sha for both, so a
  landing-page change and an app change roll back together and nothing is copied by hand onto the server.
- **Deploy** = `./deploy.sh <sha>` on the instance: pull both images, migrate, `up -d`, wait for `/health/ready`
  (checked from the edge container: the app image has no shell). Rollback = `./deploy.sh <previous sha>`;
  migrations stay applied, which is why they must be backward compatible (ADR-0011). Manual on purpose for now:
  a few deploys a week, by one person, with a check of the result. GitHub Actions deploying over SSH is a
  follow-up once the release cadence justifies a deploy key on the server.
- **Secrets** live in `deploy/scaleway/app.env` on the instance (`chmod 600`, git-ignored), never in the image
  or the repository. A secret manager is a follow-up when a second person operates production.
- **Logs**: JSON to stdout (ADR-0011), Docker's json-file driver with rotation (5 × 20 MB per container).
  `docker compose logs` is enough for a handful of players; Scaleway Cockpit or another collector when it is not.
- **Uptime**: an external HTTP check on `https://app.…/health/ready` (any free monitor) that alerts by email.

## Alternatives considered

- **Scaleway Serverless Containers.** No server to patch and TLS included, but scale-to-zero stops the import
  worker; pinning min-scale = 1 costs as much as the instance and still restarts containers at will. Worth
  revisiting if the worker moves out of process.
- **Clever Cloud.** French, fully managed (deploy from git, managed PostgreSQL with backups), the best
  zero-ops option; about twice the monthly cost for the same size. The fallback if operating a server
  becomes a burden.
- **Railway / Fly.io / Render.** Pleasant deploys and EU regions, but US companies (transfers and sub-processors
  to explain on the privacy page), and per-usage pricing that is harder to bound.
- **Bare VPS with PostgreSQL in Docker.** Cheapest, but backups, restores, upgrades and disk monitoring of the only
  stateful component become ours. A managed database is the one piece worth paying for.
- **Kubernetes (Kapsule).** Orchestration for one container is cost and cognitive load with no benefit today.

## Consequences

- The instance is ours to keep patched: unattended security upgrades, SSH by key only, port 22 restricted in the
  Scaleway security group. Docker publishes ports past `ufw`, so the **security group is the firewall**.
- A single instance means a deploy has a few seconds of downtime while the app container is replaced, and an
  instance failure means downtime until it is recreated (runbook: ~15 minutes, the data is in the managed
  database). Accepted for a beta; blue-green behind Caddy is possible later without changing the app.
- Caddy keeps its certificates in a volume (`caddy-data`); losing it only means new certificates are issued
  (Let's Encrypt rate limits: avoid rebuilding the instance in a loop).
- A restore must be **tested** before the first players arrive, not discovered on the day it is needed
  (runbook `docs/runbooks/scaleway-deploy.md`).
- The privacy page can say: hosting and database in France (Scaleway, Paris), backups 7 days.
