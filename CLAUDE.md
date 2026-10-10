# TalentSync

Portfolio project: an event-driven integration that syncs candidate data from an ATS (Teamtailor) to a CRM
and to a consultant matching platform, with LLM enrichment of consultant profiles.
It is a simplified model of a real-world integration. Not production code, but it must behave correctly
under failure: duplicates, retries, crashes, outages.

- Business context, glossary, domain rules D1–D6: @docs/domain.md
- Containers, repository layout, dependency rules, key flow, limitations: @docs/architecture.md

## Where things are

| Document | Kind | Loaded |
|---|---|---|
| `docs/domain.md` | Normative: business language and domain rules | Always (imported above) |
| `docs/architecture.md` | Dependency rules are normative; layout and flows are descriptive | Always (imported above) |
| `docs/acceptance-scenarios.md` | Normative: expected behaviour, one integration test per scenario | Open it before writing or changing tests or behaviour |
| `docs/adr/` | Normative: decisions and rationale | **Not written yet.** The ADR table in `architecture.md` is the summary. Do not create ADRs |
| `.claude/rules/invariants.md` | Normative | Always |
| Other `.claude/rules/*.md` | Rules for specific paths (`paths:` frontmatter) | When a matching file is read or written |
| `simulators/CLAUDE.md` | Rules for the fake external systems | When a file in `simulators/` is read or written |
| `.claude/settings.json`, `.claude/hooks/` | Enforced by the harness: permissions, and a hook that reports forbidden names and direct clock use after every edit | Always, as configuration, not as text |
| `README.md`, `docs/diagrams.md` | For humans, descriptive: overview, mermaid diagrams | Not needed for work. Keep them true when the layout or a flow changes |

Path-scoped rules load only when a matching file is touched, so the first file created in a directory would be
written without them. **Before creating the first file in a project, read the rule files whose `paths:` cover it.**

## How to work in this repo

- **Small steps.** One concern per change. Stop after each step and summarise what changed and why.
- Run `dotnet build` and `dotnet test` before saying something works. Never claim success without running them.
- Do not add NuGet packages, new projects or project references without asking first. Say which and why.
- Do not commit. I review and commit myself.
- Do not edit `docs/domain.md` rules, `docs/acceptance-scenarios.md` or ADRs without asking.
- Some of these rules are also enforced in `.claude/settings.json`. A denied command is a rule, not an obstacle:
  never look for a workaround, say what you wanted to run and why. A "Rule check failed" message from the hook
  means the code you just wrote breaks a rule: fix the code, never the hook.
- When a distributed-systems edge case is involved (duplicates, ordering, crash between two steps,
  timeout after the effect happened), name it explicitly in the explanation.
- If something is uncertain (SDK behaviour, emulator limitation, Teamtailor API detail), say so.
  Do not guess. Facts about Teamtailor live in `.claude/rules/teamtailor.md`.
- Never hide complexity behind a library without saying so.

## When docs and code disagree

Never resolve a mismatch silently. Say which document and which code disagree.

- **Normative** docs define intended behaviour: `.claude/rules/invariants.md`, `docs/adr/`, domain rules in
  `docs/domain.md`, `docs/acceptance-scenarios.md`, dependency rules in `docs/architecture.md`.
  Code that contradicts them is a bug. Do not change the doc to match the code.
- **Descriptive** docs describe what exists: repository layout, table columns, class and configuration names,
  skeleton pseudocode once it is implemented. If they differ from the code, the code is current.
  Update the doc in the same change.
- If unsure which kind applies, treat it as normative and ask.

## Acceptance scenarios

- Every scenario in `docs/acceptance-scenarios.md` has exactly one integration test named after its ID
  (details in `.claude/rules/testing.md`). Manual scenarios have none.
- Never change a scenario's Then to make a test pass. If you think the scenario is wrong, say so and stop.

## Naming

The repository is public. Use only neutral names:

| Use | Never use |
|---|---|
| broker, the business | real company names |
| Matching Platform, `MatchingPlatformClient` | names of real internal platforms |
| account manager | company-specific role names |
| CRM, `CrmClient` | vendor names for the CRM |

Teamtailor is a public third-party SaaS and keeps its name.

**Ports are named after the need, adapters after the provider.** `IRecruitmentSource` (port) is implemented by
`TeamtailorRecruitmentSource` (adapter). The word "Teamtailor" never appears in `*.Domain` or `*.Application`.
Inside `src/` it may appear only in `TalentSync.Infrastructure.Teamtailor`, in the three hosts (DI registration,
configuration, webhook route) and as the envelope `Source` value. `simulators/FakeTeamtailor` and test projects
are outside this rule.

## Tech stack

- .NET 10, C# 14. Primary constructors (their parameters are not readonly), keyed services,
  `TimeProvider` (never `DateTime.UtcNow` directly), `IExceptionHandler`.
- SQL Server 2025 via EF Core. One schema per container: `ingress`, `sync`, `matching`.
- `Azure.Messaging.EventHubs` directly: `EventProcessorClient` + Blob checkpoint store (Azurite).
  **No MassTransit or other messaging abstraction.**
- Resilience: `Microsoft.Extensions.Http.Resilience` (Polly v8). Who retries what and how failures are
  classified: `.claude/rules/resilience.md`.
- LLM: `Microsoft.Extensions.AI` `IChatClient`. Gemini by default, `FakeChatClient` in tests and chaos scenarios.
- Tests: xUnit v3 on Microsoft.Testing.Platform (opted in via `global.json`; VSTest syntax such as
  `--filter "FullyQualifiedName~..."` does not work). How integration tests run the system (docker compose, or in-process hosts with Testcontainers)
  is an open decision, see `.claude/rules/testing.md`.

## Configuration keys

Proposed names (descriptive: once implemented, the code is the reference).

| Key | Used by | Notes |
|---|---|---|
| `ConnectionStrings:Sql` | all three hosts | each host uses only its own schema |
| `EventHubs:ConnectionString`, `EventHubs:HubName` | all three hosts | hub `ats-events` |
| `EventHubs:ConsumerGroup` | workers | `crm-sync` / `matching` |
| `Checkpoints:BlobConnectionString`, `Checkpoints:Container` | workers | Azurite locally |
| `Teamtailor:BaseUrl`, `Teamtailor:ApiKey`, `Teamtailor:ApiVersion` | workers | API version `20240904` |
| `Teamtailor:SignatureKey` | Ingress.Api | must equal `FakeTeamtailor:SignatureKey` |
| `Teamtailor:QualifiedStageName` | Matching.Worker | `Qualified` locally |
| `Crm:BaseUrl` | Sync.Worker | |
| `MatchingPlatform:BaseUrl` | Matching.Worker | |
| `Llm:Provider` | Matching.Worker | `Gemini` or `Fake` |
| `Llm:Gemini:ApiKey` | Matching.Worker | user-secrets only, never in the repo |
| `Llm:Fake:Mode` | Matching.Worker | `Valid`, `InvalidJson`, `OutOfRange`, `Hang` |

## Current stage: MVP

In scope, minimal version of everything:
docker compose (Event Hubs emulator, Azurite, SQL Server), the three simulators, Ingress with outbox and relay,
Sync.Worker (inbox, ACL, CRM upsert, resilience, parking lot), Matching.Worker (Qualified rule, LLM enrichment
with stored results and fallback, profile upsert). Teamtailor signature v2 verification is in scope but done last.

Matching.Worker ignores `candidate.*` events in the MVP (rule D6).

Out of scope until asked: OpenTelemetry dashboards, Kubernetes manifests, admin API / JWT,
reconciliation job, parking lot replay endpoint, archiving profiles on candidate delete,
architecture tests.

## Commands

- `docker compose -f deploy/docker-compose.yml up -d`
- Reset local state (SQL data, checkpoints): `docker compose -f deploy/docker-compose.yml down -v`
- `dotnet build`
- `dotnet test` (exit code 8 = no tests ran, e.g. a project without tests yet or a filter that matched nothing)
- One scenario: `dotnet test --project tests/TalentSync.IntegrationTests --filter-method "*.S2_*"`
- Unit tests only: `dotnet test --project tests/TalentSync.Matching.Domain.Tests`
- EF migration, example for Sync (descriptive: verify once the first `DbContext` exists; needs the `dotnet-ef`
  tool and `Microsoft.EntityFrameworkCore.Design`, ask before adding them):
  `dotnet ef migrations add <Name> --project src/Sync/TalentSync.Sync.Infrastructure --startup-project src/Sync/TalentSync.Sync.Worker --context SyncDbContext`
- Gemini key (I set it myself): `dotnet user-secrets set "Llm:Gemini:ApiKey" "<key>" --project src/Matching/TalentSync.Matching.Worker`
