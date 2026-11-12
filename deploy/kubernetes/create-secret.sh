#!/usr/bin/env bash
# Creates or updates the Secret `talentsync` that the chart reads its connection strings and keys from (ADR 011).
# The chart only references the Secret by name; in Azure it would be synced from Key Vault instead.
# Safe to run again: the Secret is applied, not created. Pods read it only on start, so when a value changes the
# hosts already running are restarted.
#
# Reads deploy/infra/.env (gitignored): SQL_SA_PASSWORD, and ANTHROPIC_API_KEY if Matching.Worker runs with
# `llm.provider=Anthropic`. Everything else is a local value that is already in the repository: the emulator and
# Azurite keys are public, the Teamtailor keys are the ones FakeTeamtailor accepts and signs with.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
context=kind-talentsync

set -a
# shellcheck source=/dev/null
. "$root/deploy/infra/.env"
set +a
: "${SQL_SA_PASSWORD:?is not set in deploy/infra/.env}"

# Pods reach docker compose through host.docker.internal on the published ports (ADR 011). The hosts run as
# Production in the cluster, so nothing comes from appsettings.Development.json.
azurite_key="Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="
# The migrations Job runs sqlcmd, which takes the parts, not a connection string. Both come from these variables, so
# they cannot point at different databases.
sql_server="host.docker.internal,1433"
sql_database=TalentSync
sql_user=sa
args=(
  --from-literal=sql-connection-string="Server=$sql_server;Database=$sql_database;User Id=$sql_user;Password=$SQL_SA_PASSWORD;TrustServerCertificate=True"
  --from-literal=sql-server="$sql_server"
  --from-literal=sql-database="$sql_database"
  --from-literal=sql-user="$sql_user"
  --from-literal=sql-password="$SQL_SA_PASSWORD"
  --from-literal=eventhubs-connection-string="Endpoint=sb://host.docker.internal;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
  --from-literal=checkpoints-connection-string="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=$azurite_key;BlobEndpoint=http://host.docker.internal:10000/devstoreaccount1;"
  --from-literal=teamtailor-api-key=fake-api-key
  --from-literal=teamtailor-webhook-signature-key=fake-webhook-signature-key
)
if [ -n "${ANTHROPIC_API_KEY:-}" ]; then
  args+=(--from-literal=anthropic-api-key="$ANTHROPIC_API_KEY")
fi

# create --dry-run | apply: `create` builds the Secret from literals, `apply` creates or updates it. The values travel
# through the pipe, never as a file on disk.
result=$(kubectl --context "$context" create secret generic talentsync "${args[@]}" --dry-run=client -o yaml \
  | kubectl --context "$context" apply -f -)
echo "$result"

if [[ "$result" == *configured* ]]; then
  workloads=$(kubectl --context "$context" get deployment,statefulset -l app.kubernetes.io/part-of=talentsync -o name)
  if [ -n "$workloads" ]; then
    echo "Restarting the hosts, so they read the new values:"
    # shellcheck disable=SC2086
    kubectl --context "$context" rollout restart $workloads
  fi
fi
