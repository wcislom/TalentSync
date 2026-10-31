# Architecture Decision Records

Normative: decisions and rationale. Format: Michael Nygard (Title, Status, Context, Decision, Consequences),
plus Alternatives considered.

| ADR | Decision | Status |
|---|---|---|
| [001](001-project-structure.md) | Project structure: by context first, then by layer; a project wherever the compiler should enforce a rule; no Common project | Accepted |
| [002](002-event-hubs-over-service-bus.md) | Event Hubs over Service Bus: replay, independent consumers; cost: no DLQ → parking lot and failure classification | Accepted |
| [003](003-notify-then-fetch.md) | Notify-then-fetch: event is a signal, API state is the truth (ordering, partial payloads, unclear redelivery) | Accepted |
| [004](004-transactional-outbox.md) | Transactional outbox in Ingress: persist before 200, a single relay publishes; cost: 503 when SQL is down loses the event | Accepted |
| [005](005-idempotency.md) | Idempotency: idempotent effects, per-worker inbox, checkpoint after processing, stored LLM results | Accepted |
| [006](006-partition-key-candidate-id.md) | Partition key = `candidateId`, consequences for scaling and the job application payload assumption | Accepted |
| [007](007-llm-enrichment.md) | LLM enrichment: own port over `IChatClient` (Claude via the Anthropic SDK), PII masking, validation, short retries in the provider SDK inside our timeout, fallback to `NotEnriched`, enrichment inline in the handler | Accepted |
| [008](008-source-of-truth-and-sync-direction.md) | Source of truth and sync direction: one way from Teamtailor, ownership per field, full replace of the fields we own (last write wins) | Accepted |
| [009](009-prompt-evaluation.md) | Offline evaluation of the enrichment prompt: golden set of fictional CVs, metrics in code, report per `(PromptVersion, GoldenSetHash)`, run manually never in CI; a CI test only checks that a report exists for the current pair | Accepted |
| [010](010-observability.md) | Observability: OpenTelemetry over OTLP, Aspire Dashboard locally, one `Infrastructure.Observability` project referenced by the hosts, trace context propagated explicitly through the outbox and `EventData` properties, one consumer span per attempt | Accepted |
| [011](011-kubernetes-deployment.md) | Kubernetes deployment: only our hosts in the cluster (kind locally), infrastructure in docker compose, one Helm chart, relay switched by `OutboxRelay:Enabled` and run as a one-replica StatefulSet, migrations as a Helm hook Job before any host rolls out | Accepted |
