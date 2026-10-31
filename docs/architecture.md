# TalentSync architecture

Business context, glossary and domain rules D1–D6: `docs/domain.md`.
Expected behaviour and failure scenarios: `docs/acceptance-scenarios.md`.
Mermaid diagrams of the containers and the key flow (for humans): `docs/diagrams.md`.
Repository layout (projects, folders, tools): `docs/repository-layout.md`. ADR summaries: `docs/adr/README.md`.

The **dependency rules** below are normative. Layout, flows and names are descriptive: once code exists, the code
is current (see "When docs and code disagree" in `CLAUDE.md`).

## Containers

Three deployables, split by runtime behaviour, not by business capability. They share code and are released together.

| Container | Responsibility | Does not | Owns |
|---|---|---|---|
| **Ingress.Api** | Persist a notification envelope, return 200. Relay publishes outbox → Event Hubs | Call any API, interpret stages, hold domain logic | `ingress.*` |
| **Sync.Worker** | `candidate.*` → fetch candidate → upsert or delete CRM contact | Handle application events | `sync.*`, checkpoints of `sync-worker` |
| **Matching.Worker** | `job_application.*` → qualified? → fetch candidate + CV → enrichment (stored) → upsert profile | Decide matches, block on LLM failure, handle `candidate.*` (D6) | `matching.*`, checkpoints of `matching-worker` |
| SQL Server | Three schemas, one owner each | Act as a channel between workers | |
| Event Hubs | Hub `ats-events`, ordered log per partition, independent read position per consumer group | Dead-letter (there is no DLQ) | |
| Azurite | Checkpoints and partition ownership | | |
| Simulators | Stand-ins for Teamtailor, CRM, Matching Platform; break on demand | Production behaviour | in-memory |
| Aspire Dashboard | Local view of the hosts' traces, metrics and logs over OTLP (ADR 010) | Run in tests or CI | in-memory |

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
  IConfiguration)` for the workers (registers `IRecruitmentSource`), plus what Ingress needs, registered by
  `AddTeamtailorWebhooks`: the webhook parser `IWebhookParser` and the result type it returns, and
  `RequireTeamtailorSignature(RouteHandlerBuilder)`, the endpoint filter that verifies the signature on the webhook
  route. The filter makes the project reference the ASP.NET Core shared framework. Implementations and JSON:API DTOs are `internal`. This is the only place that defines the public surface.
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

1. FakeTeamtailor sends a `job_application.update`. Ingress inserts it into `ingress.Outbox`, returns 200.
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
