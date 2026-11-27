# Running locally

Requires .NET 10 SDK and Docker.

Tests need nothing else: the integration tests start SQL Server, Azurite and the Event Hubs emulator with
Testcontainers and run the hosts and simulators in process.

```bash
dotnet test
```

For a manual run, set the SQL password once in `deploy/infra/.env` (copy `deploy/infra/.env.example`) and give the three hosts
the connection string in user secrets:

```bash
for p in Ingress/TalentSync.Ingress.Api Sync/TalentSync.Sync.Worker Matching/TalentSync.Matching.Worker; do
  dotnet user-secrets set "ConnectionStrings:Sql" \
    "Server=localhost,1433;Database=TalentSync;User Id=sa;Password=<SQL_SA_PASSWORD>;TrustServerCertificate=True" \
    --project src/$p
done
```

`Llm:Provider` is `Fake` by default, so the system runs without an LLM key. For Claude, keep the key in user secrets
and switch the provider (every real call is billed):

```bash
dotnet user-secrets set "Llm:Anthropic:ApiKey" "<key>" --project src/Matching/TalentSync.Matching.Worker
dotnet user-secrets set "Llm:Provider" "Anthropic" --project src/Matching/TalentSync.Matching.Worker
```

To click through the whole flow by hand (success, CRM outage, candidate without a CV, poison event), run
`deploy/local/run.sh` (Git Bash, Node 22.22.3+, 24.15+ or 26). It starts the containers, builds, applies the EF
migrations and starts the three hosts and the Admin app (on Windows each in its own Windows Terminal tab). Then open
the Scenarios page of the Admin app at [http://localhost:4200](http://localhost:4200) (ADR 012). Traces, metrics and
logs of the three hosts are in the Aspire Dashboard at [http://localhost:18888](http://localhost:18888) (ADR 010).

## In a local Kubernetes cluster

Requires [kind](https://kind.sigs.k8s.io/), Helm and `kubectl`. Only the three hosts run in the cluster; SQL Server,
Event Hubs, Azurite, the simulators and the Aspire Dashboard stay in docker compose (ADR 011).

```bash
docker compose -f deploy/infra/docker-compose.yml up -d
kind create cluster --name talentsync --config deploy/kubernetes/kind/cluster.yaml
deploy/kubernetes/deploy.sh
```

`deploy.sh` creates the Secret from `deploy/infra/.env`, builds the images, loads them into kind and runs
`helm upgrade --install`, which applies the migrations before any host starts. Run it again after a code change.
Extra arguments go to Helm, e.g. `deploy/kubernetes/deploy.sh --set llm.provider=Anthropic` (needs
`ANTHROPIC_API_KEY` in `deploy/infra/.env`; every real call is billed).

Ingress.Api listens on host port 5065 in both modes, so stop the `dotnet run` hosts first. The Admin app (`npm start`
in `src/Admin/TalentSync.Admin.Web`) and the Aspire Dashboard work the same way. To clean up:

```bash
helm uninstall talentsync --kube-context kind-talentsync   # remove the hosts, keep the cluster
kind delete cluster --name talentsync                      # remove the cluster
```

## After a Docker restart

The Event Hubs emulator loses its events on restart, but the checkpoints in Azurite survive. A checkpoint then points
past the end of the new log, and the workers log `The supplied offset '…' is invalid` and read nothing from that
partition, in both modes. Reset both containers and restart the workers:

```bash
docker compose -f deploy/infra/docker-compose.yml up -d --force-recreate -V azurite eventhubs
kubectl --context kind-talentsync rollout restart deployment/sync-worker deployment/matching-worker   # in kind
```

With `dotnet run`, restart the two worker hosts instead. Events published before the reset are gone; run
the scenarios again (each run uses a new candidate id).
