# TalentSync repository layout

Descriptive: once code exists, the code is current. Keep this file true when a project, folder or tool is added,
moved or removed. Dependency rules between these projects: `docs/architecture.md` (normative).

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
                                            webhook parsing, signature v2 endpoint filter,
                                            public AddTeamtailorWebhooks() (Ingress)
                                            and AddTeamtailorApi() (workers). → Recruitment.Domain
    TalentSync.Infrastructure.Observability AddObservability(): OpenTelemetry traces, metrics and logs over OTLP,
                                            subscribes to every TalentSync.* ActivitySource and Meter, HTTP and SQL
                                            instrumentation, SpanExportPolicy (ADR 010).
                                            Only hosts reference it. References nothing in src/
    TalentSync.Infrastructure.Messaging     EventProcessorClient hosting, consumer handler skeleton (retry loop,
                                            failure classification), HandlerResult, Inbox and ParkingLot entities
                                            with schema-parameterised EF config. → Contracts
  Ingress/
    TalentSync.Ingress.Api                  host: webhook endpoint, envelope, IngressDbContext + Outbox, relay and
                                            EventHubProducerClient (only when OutboxRelay:Enabled), /health/live.
                                            → Contracts, Infrastructure.Teamtailor
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
  FakeTeamtailor                            No references to src/.
  FakeCrm
  FakeMatchingPlatform
tests/
  TalentSync.Matching.Domain.Tests
  TalentSync.Sync.Application.Tests             use case: fetch result and CRM result → use case result
  TalentSync.Matching.Application.Tests         use case: qualified / not qualified / deleted / rejected, stored or new
                                                enrichment (stored before the upsert), failure → NotEnriched
  TalentSync.Matching.Infrastructure.Tests      Matching Platform client: request body, status mapping, resilience
  TalentSync.Infrastructure.Teamtailor.Tests    webhook parsing, stage mapping, 404 → Deleted, 4xx → Rejected
  TalentSync.Infrastructure.Messaging.Tests     consumer skeleton: inbox / parking lot mapping, failure classification, handler loop
  TalentSync.Infrastructure.Observability.Tests request span is a root with the SDK enabled, SpanExportPolicy
  TalentSync.Sync.Infrastructure.Tests          CRM client: status mapping, 404 on delete = done, resilience
  TalentSync.Matching.Evaluation.Tests          prompt evaluation: metrics, golden set hash, case files, report
  TalentSync.IntegrationTests                   acceptance scenarios (see .claude/rules/testing.md)
eval/                                       offline evaluation of the enrichment prompt (ADR 009), never run in CI
  README.md                                 what each metric catches, how to read a report
  TalentSync.Matching.Evaluation            console runner over ChatEnrichmentService. → Matching.Infrastructure
  cases/                                    golden set: one JSON per fictional CV
  reports/                                  committed reports; local/ is gitignored
  run-eval.sh                               runs the evaluation locally; provider, runs, output and model as variables at the top
deploy/                                     docker-compose.yml (infra + simulators + Aspire Dashboard), simulator.Dockerfile,
                                            eventhubs/Config.json, run-local.sh (containers, migrations, the three hosts),
                                            host.Dockerfile (one image per host), migrations.Dockerfile + migrate.sh
                                            (idempotent EF scripts applied with sqlcmd, ADR 011)
docs/                                       domain.md, architecture.md, repository-layout.md, acceptance-scenarios.md, diagrams.md,
                                            adr/ (index: adr/README.md), e2e.http (end-to-end demo by hand),
                                            images/ (screenshots for the README)
.github/workflows/ci.yml                    on PRs and main: Build job, then Test job ("Test results" check, coverage summary)
.claude/rules/                              topic rules, some load only for matching paths
.claude/settings.json                       permissions and hooks enforced by the harness
.claude/hooks/                              check-rules.sh: Teamtailor in Domain/Application, direct clock in src/
.gitattributes                              *.sh keeps LF line endings (Git Bash on Windows)
.dockerignore                               build context for simulator images (repo root)
```
