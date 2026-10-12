#!/usr/bin/env bash
# Deploys one release on the instance (ADR-0019): pull the images built by CI for that commit, apply the
# migrations, then replace the containers. Usage: ./deploy.sh <git sha>   (or "main" for the latest build)
set -euo pipefail
cd "$(dirname "$0")"

release="${1:?usage: ./deploy.sh <git sha | main>}"
test -f .env && test -f app.env || { echo "Missing .env or app.env (see env.example, app.env.example)." >&2; exit 1; }
previous="$(sed -n 's/^RELEASE=//p' .env)"

sed -i "s/^RELEASE=.*/RELEASE=${release}/" .env
echo "Deploying ${release} (was ${previous:-none})"

docker compose pull app edge
# Migrations first, while the previous version still serves: they must stay backward compatible (ADR-0011).
docker compose --profile release run --rm migrate
docker compose up -d --remove-orphans

# The app image has no shell: readiness is checked from the edge container.
for _ in $(seq 1 45); do
  if docker compose exec -T edge wget -q -O /dev/null http://app:8080/health/ready 2>/dev/null; then
    echo "Release ${release} is ready."
    docker image prune -f >/dev/null
    exit 0
  fi
  sleep 2
done

echo "Release ${release} is not ready after 90 s. Logs: docker compose logs --tail 200 app" >&2
echo "To go back: ./deploy.sh ${previous} (migrations already applied stay: they are backward compatible)." >&2
exit 1
