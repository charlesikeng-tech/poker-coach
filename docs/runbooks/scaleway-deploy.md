# Runbook — Production on Scaleway

How NutsIQ runs in production (ADR-0019): one instance with Docker Compose (Caddy + app), Scaleway Managed
PostgreSQL on a private network, images built by CI on GHCR. Domains below assume `nutsiq.com` (landing page)
and `app.nutsiq.com` (app); replace them if they differ.

Order matters: database and network first, then the instance, DNS, env files, first deploy, then the
third-party services that need the public URL (Google, Stripe, Brevo).

## 1. Scaleway project

Console → create a project `nutsiq`, region **fr-par** for everything below. Billing alerts: Billing →
Alerts, e.g. at 40 € / month, so a mistake (a forgotten bigger instance) is seen within days.

## 2. Private network

Network → VPC → `default` (fr-par) → create a **Private Network** `nutsiq-pn`. Both the database and the
instance attach to it; the database gets no public endpoint.

## 3. Managed PostgreSQL

Databases → PostgreSQL → create:

- engine: the latest PostgreSQL offered (≥ 16), node type **DB-DEV-S**, standalone (no HA for a beta);
- network: **private network `nutsiq-pn` only** — remove the public endpoint;
- database `poker_coach`, user `poker_coach`, a generated password (password manager, never in chat or git);
- **backups: automatic, retention 7 days** (the privacy page promises ≤ 30 days — never raise it above 30);
- note the **private IP** of the endpoint.

The app's `migrate` command creates every schema; nothing else to run on the database.

## 4. Instance

Compute → Instances → create:

- type **PLAY2-PICO**, image **Ubuntu 24.04 LTS**, 20 GB block volume is enough (images + logs);
- your **SSH public key** (Project → SSH keys) — no password login;
- attach **`nutsiq-pn`**; keep a public flexible IPv4 (note it);
- **security group** `nutsiq-web`: inbound default *drop*; allow TCP 80, TCP 443, UDP 443 from anywhere;
  TCP 22 **from your IP only**. Docker publishes ports past `ufw`, so this group is the firewall.

Then, over SSH (`ssh root@<ip>`):

```bash
apt-get update && apt-get -y upgrade
apt-get -y install unattended-upgrades && dpkg-reconfigure -f noninteractive unattended-upgrades
curl -fsSL https://get.docker.com | sh          # Docker Engine + Compose plugin
docker compose version
ip -br addr                                       # the private network interface must have an address
```

Check the instance reaches the database privately (`nc -zv <db-private-ip> 5432`, from `apt install
netcat-openbsd`).

## 5. Code and registry access

The images are private on GHCR. On GitHub: Settings → Developer settings → Personal access tokens →
**Tokens (classic)** → scope **`read:packages`** only, expiry 1 year (calendar reminder to renew).

```bash
echo '<token>' | docker login ghcr.io -u <github-user> --password-stdin
git clone https://github.com/charlesikeng-tech/poker-coach.git /opt/nutsiq   # or copy deploy/scaleway only
cd /opt/nutsiq/deploy/scaleway
```

Only `deploy/scaleway/` is used on the server; the clone is just a convenient way to get it and update it
(`git pull`).

## 6. Env files

```bash
cp env.example .env && cp app.env.example app.env
chmod 600 .env app.env
```

- `.env`: domains, ACME e-mail, image names (keep them lowercase). `RELEASE` is set by `deploy.sh`.
- `app.env`: connection string with the **private IP** and `SSL Mode=Require`; Google, Anthropic, Brevo, Stripe
  **live** values; `Admin__Emails__0`. Use **new keys** for production — never the ones used locally.

## 7. DNS

At the registrar of `nutsiq.com`:

| Name | Type | Value |
|---|---|---|
| `@` | A | instance public IP |
| `www` | A (or CNAME → `nutsiq.com`) | instance public IP |
| `app` | A | instance public IP |

Plus the **Brevo** records (domain authentication: DKIM, DMARC, Brevo code) from Brevo → Senders, domains →
Domains → Authenticate. Wait until `dig +short app.nutsiq.com` returns the IP before the first deploy: Caddy
asks Let's Encrypt for certificates on start, and repeated failures hit rate limits.

## 8. First deploy

Pick the sha of a green CI run on `main` (GitHub → Actions; the images exist only for pushes to `main`):

```bash
./deploy.sh <sha>
```

It pulls both images, runs the migrations, starts Caddy and the app, and waits for `/health/ready`. Then:

```bash
curl -I https://nutsiq.com                    # 200, landing page
curl -I https://www.nutsiq.com                # 308 → https://nutsiq.com
curl -fsS https://app.nutsiq.com/health/ready
docker compose ps && docker compose logs --tail 50 app
```

## 9. Third-party services (need the public URL)

- **Google** (Cloud console → Credentials → the OAuth client): authorised redirect URI
  `https://app.nutsiq.com/signin-google`, JavaScript origin `https://app.nutsiq.com`. Publish the consent screen
  (production) before inviting players outside the test users list.
- **Stripe live** — redo `docs/runbooks/stripe-setup.md` in Live mode: products and prices, portal, webhook
  `https://app.nutsiq.com/api/billing/webhook`, restricted live key; put the values in `app.env`, then
  `docker compose up -d app` to reload.
- **Brevo**: domain authenticated (step 7), sender `no-reply@nutsiq.com` validated; create an account with a real
  address and check the confirmation e-mail arrives (and not in spam).
- **Anthropic**: a production API key with a spending limit in the console, above `Coaching__MonthlyBudgetUsd`.

## 10. Before inviting players

- [ ] `PRIVACY_CONTACT_EMAIL` set in `web/src/app/features/privacy/privacy-page.ts` (a release is needed).
- [ ] Legal notice (`site/mentions-legales.html`): publisher fields and the host's phone number filled.
- [ ] CGV reviewed; VAT status decided (Stripe Tax or not).
- [ ] **Restore tested** (below) at least once.
- [ ] Uptime check on `https://app.nutsiq.com/health/ready` (e.g. UptimeRobot or Better Stack free plan),
      alerting by e-mail.
- [ ] An administrator account works (`Admin__Emails__0`), the admin page opens.
- [ ] End to end: sign up, import a file, statistics appear, an AI explanation, a Stripe checkout in live mode with
      a real card and an immediate refund.

## Routine operations

**Deploy a release** — after CI is green on `main`:

```bash
cd /opt/nutsiq/deploy/scaleway && git pull && ./deploy.sh <sha>
```

`git pull` only matters when `deploy/scaleway/` changed (compose, script). A few seconds of downtime while the
app container is replaced.

**Roll back** — `./deploy.sh <previous sha>` (printed by a failed deploy). Migrations stay applied; they are
backward compatible by rule (ADR-0011). A migration that is not must be called out in its PR, with its own plan.

**Logs** — `docker compose logs -f app` (JSON lines; `| jq` helps), `docker compose logs edge` for TLS issues.

**Change a secret or a limit** — edit `app.env`, then `docker compose up -d app`.

**Disk** — `df -h /`; images are pruned after each successful deploy, logs are capped at 100 MB per container.

**Updates** — security updates are automatic; reboot when `/var/run/reboot-required` exists (a few seconds
of downtime, containers restart by themselves).

## Restore test (do it before launch, then every quarter)

1. Databases → the instance → Backups → pick the latest automatic backup → **Restore** into a **new
   database** of the same instance, e.g. `poker_coach_restore` (never over `poker_coach` for a test).
2. From the instance:
   ```bash
   docker run --rm -it --network host postgres:18-alpine \
     psql "host=<db-private-ip> dbname=poker_coach_restore user=poker_coach sslmode=require" \
     -c "select count(*) from identity.users;" -c "select count(*) from poker.hands;"
   ```
   Counts should match production within the backup's age (`\dn` and `\dt <schema>.*` list the rest).
3. Delete `poker_coach_restore`. Note the date and the time it took in this runbook's log below.

**Real restore** (data loss or bad migration): restore the backup into a new database, point
`ConnectionStrings__PokerCoach` in `app.env` at it, `docker compose up -d app`, then check `/health/ready`.
Everything written since the backup is lost; say so to the players concerned.

## Rebuild the instance

The instance holds no data. If it is lost: steps 4 to 6 on a new instance, the same env files (keep an
encrypted copy of `.env` and `app.env` in the password manager), move the public IP or update the DNS,
`./deploy.sh <last sha>`. About 15 minutes.

## Log

| Date | What | Notes |
|---|---|---|
| | First restore test | |
