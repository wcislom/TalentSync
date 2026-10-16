#!/usr/bin/env bash
# Starts everything for a local run: docker compose (infrastructure and simulators), build, EF migrations, then
# Ingress.Api, Sync.Worker and Matching.Worker. On Windows each host gets its own Windows Terminal tab (stop it with Ctrl+C there);
# elsewhere they run in this terminal and Ctrl+C stops all three.
# Containers keep running: docker compose -f deploy/docker-compose.yml stop
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

titles=(Ingress.Api Sync.Worker Matching.Worker)
projects=(src/Ingress/TalentSync.Ingress.Api src/Sync/TalentSync.Sync.Worker src/Matching/TalentSync.Matching.Worker)

echo "Starting containers..."
docker compose -f deploy/docker-compose.yml up -d --wait
# The emulator loses its events on every restart while Azurite keeps the checkpoints, so a checkpoint can point past
# the end of a partition ("The supplied offset is invalid"). Start both clean on every run. SQL keeps its data.
# --renew-anon-volumes: Azurite keeps its data in an anonymous volume, which --force-recreate alone reattaches.
docker compose -f deploy/docker-compose.yml up -d --wait --force-recreate --renew-anon-volumes azurite eventhubs

# One build before the three `dotnet run --no-build`: three parallel builds of the shared projects collide on files.
# A host that is still running locks its DLLs; the build then fails and names it.
echo "Building..."
dotnet build TalentSync.slnx --nologo --verbosity quiet

# Before the hosts start, so neither the relay nor a worker meets a missing table. Applies only pending migrations.
# The connection string comes from each host's user secrets (README, "Running locally").
echo "Applying migrations..."
dotnet tool restore >/dev/null
dotnet ef database update --no-build --project src/Ingress/TalentSync.Ingress.Api --context IngressDbContext
dotnet ef database update --no-build --project src/Sync/TalentSync.Sync.Infrastructure \
  --startup-project src/Sync/TalentSync.Sync.Worker --context SyncDbContext
dotnet ef database update --no-build --project src/Matching/TalentSync.Matching.Infrastructure \
  --startup-project src/Matching/TalentSync.Matching.Worker --context MatchingDbContext

if command -v wt.exe >/dev/null 2>&1; then
  for i in "${!titles[@]}"; do
    # MSYS_NO_PATHCONV: Git Bash would otherwise turn `/k` into a Windows path.
    MSYS_NO_PATHCONV=1 wt.exe -w talentsync new-tab --title "${titles[$i]}" -d "$(cygpath -w "$root")" \
      cmd /k dotnet run --no-build --launch-profile http --project "${projects[$i]}"
    echo "Started ${titles[$i]}"
  done
  echo "Click through the flow with docs/e2e.http. Traces and logs: http://localhost:18888"
else
  trap 'kill $(jobs -p) 2>/dev/null' INT TERM EXIT
  for i in "${!titles[@]}"; do
    dotnet run --no-build --launch-profile http --project "${projects[$i]}" 2>&1 | sed -u "s/^/[${titles[$i]}] /" &
    echo "Started ${titles[$i]}"
  done
  echo "Click through the flow with docs/e2e.http. Traces and logs: http://localhost:18888. Ctrl+C stops all three hosts."
  wait
fi
