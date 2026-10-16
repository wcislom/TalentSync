# TalentSync architecture

Business context, glossary and domain rules D1–D6: `docs/domain.md`.
Expected behaviour and failure scenarios: `docs/acceptance-scenarios.md`.
Mermaid diagrams of the containers and the key flow (for humans): `docs/diagrams.md`.

The **dependency rules** below are normative. Layout, flows and names are descriptive: once code exists, the code
is current (see "When docs and code disagree" in `CLAUDE.md`).

## Containers

Three deployables, split by runtime behaviour, not by business capability. They share code and are released together.

| Container | Responsibility | Does not | Owns |
|---|---|---|---|
| **Ingress.Api** | Verify signature, persist a notification envelope, return 200. Relay publishes outbox → Event Hubs | Call any API, interpret stages, hold domain logic | `ingress.*` |
| **Sync.Worker** | `candidate.*` → fetch candidate → upsert or delete CRM contact | Handle application events | `sync.*`, checkpoints of `sync-worker` |
| **Matching.Worker** | `job_application.*` → qualified? → fetch candidate + CV → enrichment (stored) → upsert profile | Decide matches, block on LLM failure, handle `candidate.*` (D6) | `matching.*`, checkpoints of `matching-worker` |
| SQL Server | Three schemas, one owner each | Act as a channel between workers | |
| Event Hubs | Hub `ats-events`, ordered log per partition, independent read position per consumer group | Dead-letter (there is no DLQ) | |
| Azurite | Checkpoints and partition ownership | | |
| Simulators | Stand-ins for Teamtailor, CRM, Matching Platform; break on demand | Production behaviour | in-memory |
| Aspire Dashboard | Local view of the hosts' traces, metrics and logs over OTLP (ADR 010) | Run in tests or CI | in-memory |

## Repository layout

```
README.md                                   for humans: overview, running locally, simplifications
TalentSync.slnx                             solution, folders mirror the directories below
global.json                                 SDK pin, test runner = Microsoft.Testing.Platform
dotnet-tools.json                           local tool manifest: dotnet-ef (migrations, used by deploy/run-local.sh)
Directory.Build.props                       net10.0, nullable, implicit usings, warnings as errors
Directory.Packages.props                    central package versions (CPM)
src/
  Shared/
    TalentSync.Contracts                    event envelope, event names, SchemaVersion. References nothing
    TalentSync.Recruitment.Domain           our read-only model of recruitment: Candidate, JobApplication,
                                            ApplicationStatus, FetchResult<T> (Found | Deleted | Rejected),
                                            port IRecruitmentSource. References nothing
    TalentSync.Infrastructure.Teamtailor    ACL: TeamtailorRecruitmentSource (port adapter) over TeamtailorApiClient
                                            (HTTP, status mapping) and TeamtailorMapper, internal JSON:API DTOs, 429 handling,
                                            stage mapping from config, 404 → Deleted, 4xx / unmappable → Rejected,
                                            signature v2, webhook parsing, public AddTeamtailorWebhooks() (Ingress)
                                            and AddTeamtailorApi() (workers). → Recruitment.Domain
    TalentSync.Infrastructure.Observability AddObservability(): OpenTelemetry traces, metrics and logs over OTLP,
                                            subscribes to every TalentSync.* ActivitySource and Meter, HTTP and SQL
                                            instrumentation, SpanExportPolicy (ADR 010).
                                            Only hosts reference it. References nothing in src/
    TalentSync.Infrastructure.Messaging     EventProcessorClient hosting, consumer handler skeleton (retry loop,
                                            failure classification), HandlerResult, Inbox and ParkingLot entities
                                            with schema-parameterised EF config. → Contracts
  Ingress/
    TalentSync.Ingress.Api                  host: webhook endpoint, envelope, IngressDbContext + Outbox, relay,
                                            EventHubProducerClient. → Contracts, Infrastructure.Teamtailor
  Sync/
    TalentSync.Sync.Application             UseCases/SyncCandidateToCrmUseCase, Ports/ICrm. → Recruitment.Domain
    TalentSync.Sync.Infrastructure          CrmClient + resilience, SyncDbContext. → Sync.Application, Infrastructure.Messaging
    TalentSync.Sync.Worker                  host: candidate.* handler (use case result → HandlerResult),
                                            composition root, group "sync-worker". Web host: only /health/live
                                            (liveness for an orchestrator, no dependency checks)
  Matching/
    TalentSync.Matching.Domain              ConsultantProfile, CandidateId, Skill, Confidence, Seniority,
                                            Enrichment (+ TryCreate → EnrichmentValidation), EnrichmentStatus. References nothing
    TalentSync.Matching.Application         UseCases/CreateConsultantProfileUseCase, Ports/IMatchingPlatform,
                                            IEnrichmentService, IEnrichmentResultStore, CvHash.
                                            → Matching.Domain, Recruitment.Domain
    TalentSync.Matching.Infrastructure      Llm/ (CvMasker, EnrichmentPrompt, ChatEnrichmentService over IChatClient,
                                            Fake/FakeChatClient),
                                            EnrichmentResults/, MatchingPlatformClient, MatchingDbContext with
                                            EnrichmentResultStore. → Matching.Application, Infrastructure.Messaging
    TalentSync.Matching.Worker              host: job_application.* handler (use case result → HandlerResult),
                                            composition root, group "matching-worker". Web host: only /health/live
                                            (liveness for an orchestrator, no dependency checks)
simulators/                                 fake external systems, NOT part of the product (see simulators/CLAUDE.md).
  FakeTeamtailor                            No references to src/. Signature v2 is implemented independently.
  FakeCrm
  FakeMatchingPlatform
tests/
  TalentSync.Matching.Domain.Tests
  TalentSync.Sync.Application.Tests             use case: fetch result and CRM result → use case result
  TalentSync.Matching.Application.Tests         use case: qualified / not qualified / deleted / rejected, stored or new
                                                enrichment (stored before the upsert), failure → NotEnriched
  TalentSync.Matching.Infrastructure.Tests      Matching Platform client: request body, status mapping, resilience
  TalentSync.Infrastructure.Teamtailor.Tests    signature v2, webhook parsing, stage mapping, 404 → Deleted, 4xx → Rejected
  TalentSync.Infrastructure.Messaging.Tests     consumer skeleton: inbox / parking lot mapping, failure classification, handler loop
  TalentSync.Infrastructure.Observability.Tests request span is a root with the SDK enabled, SpanExportPolicy
  TalentSync.Sync.Infrastructure.Tests          CRM client: status mapping, 404 on delete = done, resilience
  TalentSync.Matching.Evaluation.Tests          prompt evaluation: metrics, golden set hash, case files, report
  TalentSync.IntegrationTests                   acceptance scenarios (see .claude/rules/testing.md)
eval/                                       offline evaluation of the enrichment prompt (ADR 009), never run in CI
  TalentSync.Matching.Evaluation            console runner over ChatEnrichmentService. → Matching.Infrastructure
  cases/                                    golden set: one JSON per fictional CV
  reports/                                  committed reports; local/ is gitignored
  run-eval.sh                               runs the evaluation locally; provider, runs, output and model as variables at the top
deploy/                                     docker-compose.yml (infra + simulators + Aspire Dashboard), simulator.Dockerfile,
                                            eventhubs/Config.json, run-local.sh (containers, migrations, the three hosts)
docs/                                       domain.md, architecture.md, acceptance-scenarios.md, diagrams.md,
                                            adr/ (index: adr/README.md), e2e.http (end-to-end demo by hand),
                                            images/ (screenshots for the README)
.github/workflows/ci.yml                    on PRs and main: Build job, then Test job ("Test results" check, coverage summary)
.claude/rules/                              topic rules, some load only for matching paths
.claude/settings.json                       permissions and hooks enforced by the harness
.claude/hooks/                              check-rules.sh: Teamtailor in Domain/Application, direct clock in src/
.gitattributes                              *.sh keeps LF line endings (Git Bash on Windows)
.dockerignore                               build context for simulator images (repo root)
```

## Layers and projects

Clean Architecture with the hosts as the presentation layer (driving adapters + composition root).
Code is split by context first, then by layer, with a project wherever the compiler should enforce a rule.

| Layer | Shared | Ingress | Sync | Matching |
|---|---|---|---|---|
| Presentation + composition root | | `Ingress.Api` | `Sync.Worker` | `Matching.Worker` |
| Infrastructure | `Infrastructure.Teamtailor`, `Infrastructure.Messaging`, `Infrastructure.Observability` | (inside `Ingress.Api`) | `Sync.Infrastructure` | `Matching.Infrastructure` |
| Application | | | `Sync.Application` | `Matching.Application` |
| Domain | `Recruitment.Domain` | | | `Matching.Domain` |
| Contract between processes | `Contracts` | | | |

Deliberate asymmetries: Ingress has no domain and is one project; Sync has no business rules, so there is no
`Sync.Domain`.

## Dependency rules (normative)

- `*.Domain` and `Contracts` reference nothing.
- `*.Application` references only domain projects. Never infrastructure, never a host.
- `Matching.Domain` does **not** reference `Recruitment.Domain`. The two contexts don't know each other's models;
  `Matching.Application` translates between them.
- `Infrastructure.*` projects reference the layer whose ports they implement.
- Only hosts (composition roots) reference `Infrastructure.Teamtailor`. Its public surface is
  `AddTeamtailorWebhooks(IServiceCollection, IConfiguration)` for Ingress, `AddTeamtailorApi(IServiceCollection,
  IConfiguration)` for the workers (registers `IRecruitmentSource`), plus two interfaces that Ingress needs, registered by
  `AddTeamtailorWebhooks`: a webhook signature verifier and a webhook parser, and the result type the parser returns
  (proposed names: `IWebhookSignatureVerifier`, `IWebhookParser`, `WebhookNotification`).
  Implementations and JSON:API DTOs are `internal`. This is the only place that defines the public surface.
- Each worker's `DbContext` lives in its own `*.Infrastructure`, so the other worker cannot see its schema.
- Ingress does not reference `Infrastructure.Messaging`: it only produces.
- Simulators reference nothing in `src/`. Test projects may reference both.

| Rule | Enforced by |
|---|---|
| Use cases don't know HTTP, JSON:API or Teamtailor | Port `IRecruitmentSource` in `Recruitment.Domain`; only hosts reference the adapter |
| JSON:API types never leave the adapter | DTOs and implementations are `internal` |
| Bounded contexts don't share models | `Matching.Domain` does not reference `Recruitment.Domain` |
| A worker never touches another worker's schema | Each `DbContext` lives in that worker's `*.Infrastructure` |
| Consumer invariants (inbox, checkpoint, never throw, parking lot) are implemented once | `Infrastructure.Messaging` |
| The skeleton classifies failures without knowing adapters | Permanent failures are `Rejected` values translated by the host; transient ones are BCL / Polly / Azure exceptions |
| Application does not depend on Infrastructure | Project references (compiler) |
| A bug in our signature verifier is not mirrored in the signer | FakeTeamtailor implements signature v2 independently |

## Service registration (normative)

One convention in every project, host or library, so the registrations are always in the same place:

- A project that registers services has exactly one `DependencyInjection.cs` with `static class DependencyInjection`
  (`public` in libraries, `internal` in hosts) and one entry point `Add<Name>(this IServiceCollection, IConfiguration)`,
  e.g. `AddMessaging`, `AddIngress`. No `*ServiceCollectionExtensions` classes.
  One exception: `Infrastructure.Teamtailor` has `AddTeamtailorWebhooks` (Ingress) and `AddTeamtailorApi` (workers),
  in the same class, because each host needs only one half and each half validates its own configuration on start.
- `Program.cs` holds no registrations: it creates the builder, calls the host's own `Add<Host>()`, maps endpoints, runs.
- A host's `Add<Host>()` calls the `Add<Name>()` of the libraries it references; it never registers their
  `internal` types itself.

## Key flow: application qualified

1. FakeTeamtailor sends a signed `job_application.update`. Ingress verifies it, inserts into `ingress.Outbox`,
   returns 200.
2. The relay publishes the envelope (ids only) with partition key `candidateId`.
3. Matching.Worker receives it on the candidate's partition and checks `matching.Inbox`: seen → checkpoint, skip.
4. Through `IRecruitmentSource`: GET the application with its stage. Not qualified → inbox row, checkpoint.
5. GET the candidate with the CV text.
6. Look up `matching.EnrichmentResults` by `(candidateId, cvHash, promptVersion)`. Not found → the adapter masks the CV and calls
   the LLM; a valid result is stored before step 7. Failure → `NotEnriched`, nothing stored.
7. `PUT /profiles/{candidateId}` in FakeMatchingPlatform.
8. Insert the inbox row, checkpoint.

## Delivery guarantees

The rules are invariants 1–13 in `.claude/rules/invariants.md`. In short: at-least-once end to end, correctness from
idempotent effects keyed by `candidateId`, LLM results stored before the effect, ordering per candidate within a
partition. Notify-then-fetch makes stale or reordered events harmless: each worker acts on the current API state.

## Known limitations (to be covered by ADRs)

- If SQL Server is down at ingress, the webhook gets 503 and Teamtailor may not retry: the event is lost.
  A reconciliation job against the API would close this gap (out of MVP scope).
- One relay replica in the MVP; several would weaken ordering.
- Ingress does not verify the Teamtailor webhook signature (v2), so it accepts unsigned webhooks.
- Candidate id in job application webhooks is an assumption (see `.claude/rules/teamtailor.md`).
- Candidate delete: the CRM marks the contact as deleted and its personal data stays there; anonymisation in the
  CRM, and profile archiving and anonymisation in the Matching Platform, are out of MVP.
- A profile is not refreshed when the candidate's CV changes, only on the next qualification.
- If a recruiter renames the qualified stage and `Teamtailor:QualifiedStageName` is not updated, applications
  are processed as "not qualified" and no profiles are created. Nothing fails, so this is silent.
- Both workers share Teamtailor's rate limit (per API key) but handle 429 independently.
- A wrong API key (401/403) stops the partitions of the affected worker until the configuration is fixed.
  Deliberate: parking every event would only create a replay job.
- Unclassified exceptions are retried 5 times per handler invocation, then parked. The limit is counted in memory
  and does not cover a failure that kills the process.

## ADRs

Written ADRs are in `docs/adr/` (index: `docs/adr/README.md`). This table summarises all of them.

| ADR | Decision |
|---|---|
| [001](adr/001-project-structure.md) | Project structure: by context first, then by layer; a project wherever the compiler should enforce a rule; no Common project |
| [002](adr/002-event-hubs-over-service-bus.md) | Event Hubs over Service Bus: replay, independent consumers; cost: no DLQ → parking lot and failure classification |
| [003](adr/003-notify-then-fetch.md) | Notify-then-fetch: event is a signal, API state is the truth (ordering, partial payloads, unclear redelivery) |
| [004](adr/004-transactional-outbox.md) | Transactional outbox in Ingress: persist before 200, a single relay publishes; cost: 503 when SQL is down loses the event |
| [005](adr/005-idempotency.md) | Idempotency: idempotent effects, per-worker inbox, checkpoint after processing, stored LLM results |
| [006](adr/006-partition-key-candidate-id.md) | Partition key = `candidateId`, consequences for scaling and the job application payload assumption |
| [007](adr/007-llm-enrichment.md) | LLM enrichment: own port over `IChatClient` (Claude via the Anthropic SDK), PII masking, validation, short retries in the provider SDK inside our timeout, fallback to `NotEnriched`, enrichment inline in the handler |
| [008](adr/008-source-of-truth-and-sync-direction.md) | Source of truth and sync direction: one way from Teamtailor, ownership per field, full replace of the fields we own (last write wins) |
| [009](adr/009-prompt-evaluation.md) | Offline evaluation of the enrichment prompt: golden set of fictional CVs, metrics in code, report per `(PromptVersion, GoldenSetHash)`, run manually never in CI; a CI test only checks that a report exists for the current pair |
| [010](adr/010-observability.md) | Observability: OpenTelemetry over OTLP, Aspire Dashboard locally, one `Infrastructure.Observability` project referenced by the hosts, trace context propagated explicitly through the outbox and `EventData` properties, one consumer span per attempt |
