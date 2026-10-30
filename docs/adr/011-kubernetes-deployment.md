# ADR 011: Kubernetes deployment

## Status

Accepted

## Context

We want to run the hosts in a Kubernetes cluster. A cluster runs several replicas and stops pods at any time.
Two things break: the outbox relay runs inside Ingress.Api, so N API replicas would be N relays (invariant 13),
and migrations on start would race between replicas.

## Decision

- **Only our hosts run in the cluster.** SQL Server, Azurite, the Event Hubs emulator, the simulators and the Aspire
  Dashboard stay in docker compose, as managed services and external systems would in production.
- **Local cluster: kind. Manifests: one Helm chart** (`deploy/helm/talentsync`) for all hosts, with
  `values-local.yaml`. Templates stay close to plain YAML: only what differs between environments is a value
  (images, addresses, replicas); no helper library.
- **The relay is switched by `OutboxRelay:Enabled`** (default `true`, so the local run and tests do not change).
  One image, two workloads: `ingress-api` (several replicas, flag `false` in the template, not a value) and `ingress-relay`
  (a StatefulSet with one replica: unlike a Deployment, it does not start a second pod while the first may still run
  on an unreachable node).
- **Workers:** a Deployment each, at most as many replicas as partitions (4).
- **Probes:** `/health/live` only, never dependency checks: a SQL outage must not restart every pod.
- **Migrations:** an EF migration bundle per `DbContext`, run as a `pre-install` / `pre-upgrade` hook Job. Helm runs
  it before the hosts roll out and stops the upgrade if it fails.
- **Secrets** in a Kubernetes Secret created from the gitignored `deploy/.env` by the local run script, never in
  chart values; the chart only references it by name.
  A Kubernetes Secret is base64, not encryption, and readable by anyone who can read Secrets or create pods in the
  namespace. Fine for a local cluster; production would use workload identity for Azure resources (no connection
  string at all) and Key Vault for the rest.

## Consequences

- Invariant 13 depends on one line in the template. If it is missing, every API replica runs a relay and
  nothing fails.
- Stopping a worker cancels its handler; the next owner of the partition processes the event again. Safe because
  effects are idempotent and LLM results are stored (invariants 2, 3, 5).
- Ingress.Api waits briefly before stopping (`preStop`), so a webhook that arrives after SIGTERM is not refused and
  lost.
- Migrations must stay compatible with the previous code version, since old pods run during a rollout.
- Not verified yet: whether pods in kind on Docker Desktop (Windows) reach the containers in docker compose.

## Alternatives considered

- **The relay as its own host.** Rejected: two new projects, and the guarantee would still be one line in a
  manifest (`replicas: 1`).
- **SQL Server and Event Hubs in the cluster.** Rejected: production uses managed services.
- **Kustomize.** Plain YAML, built into `kubectl`. Rejected: the migration Job would need its ordering in a script,
  and there is no release history or rollback.
- **Migrations on host start.** Rejected: replicas race.
