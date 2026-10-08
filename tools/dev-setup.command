#!/bin/bash
# Local development setup, idempotent: safe to run again at any time.
#   1. starts PostgreSQL (docker compose) and waits until it is healthy
#   2. stores the connection string in the API's user secrets (password and port from .env)
#   3. installs dotnet-ef if missing
#   4. generates the InitialIdentity migration if no migration exists yet
#   5. applies migrations, builds and runs the backend tests
# Output is also written to .dev-setup.log at the repository root.
set -euo pipefail
cd "$(dirname "$0")/.."
exec > >(tee .dev-setup.log) 2>&1

step() { printf '\n==> %s\n' "$1"; }

step "Checking prerequisites"
command -v docker >/dev/null || { echo "Docker is not installed."; exit 1; }
docker info >/dev/null 2>&1 || { echo "Docker is not running: start Docker Desktop (or your Docker engine) and run this script again."; exit 1; }
command -v dotnet >/dev/null || { echo ".NET SDK is not installed."; exit 1; }
[ -f .env ] || { echo "Missing .env: copy .env.example and set POSTGRES_PASSWORD."; exit 1; }
set -a; . ./.env; set +a
POSTGRES_PORT="${POSTGRES_PORT:-5432}"

step "Starting PostgreSQL on 127.0.0.1:${POSTGRES_PORT}"
docker compose up -d postgres
container="$(docker compose ps -q postgres)"
for _ in $(seq 1 60); do
  status="$(docker inspect -f '{{.State.Health.Status}}' "$container")"
  [ "$status" = "healthy" ] && break
  sleep 1
done
[ "$status" = "healthy" ] || { echo "PostgreSQL did not become healthy (status: $status). See: docker compose logs postgres"; exit 1; }
echo "PostgreSQL is healthy."

cd backend

step "Storing the connection string in user secrets (password not shown)"
dotnet user-secrets set "ConnectionStrings:PokerCoach" \
  "Host=localhost;Port=${POSTGRES_PORT};Database=poker_coach;Username=${POSTGRES_USER:-poker_coach};Password=${POSTGRES_PASSWORD}" \
  --project src/PokerCoach.Api >/dev/null
echo "Done."

step "Checking dotnet-ef"
export PATH="$PATH:$HOME/.dotnet/tools"
if ! command -v dotnet-ef >/dev/null; then
  dotnet tool install --global dotnet-ef --version "10.*"
fi
dotnet ef --version

EF_ARGS=(--project src/PokerCoach.Infrastructure --startup-project src/PokerCoach.Api)
MIGRATIONS_DIR="src/PokerCoach.Infrastructure/Persistence/Migrations"

if [ ! -d "$MIGRATIONS_DIR" ]; then
  step "Generating migration InitialIdentity"
  dotnet ef migrations add InitialIdentity "${EF_ARGS[@]}" --output-dir Persistence/Migrations
  dotnet ef migrations script "${EF_ARGS[@]}" --idempotent --output ../.dev-setup-migration.sql
fi

step "Applying migrations"
dotnet ef database update "${EF_ARGS[@]}"

step "Building and testing"
dotnet build --nologo -v quiet
dotnet test --solution PokerCoach.slnx --no-build

step "All good"
echo "Next: Google OAuth client secrets, then 'dotnet run --project src/PokerCoach.Api' and 'npm start' in web/."
