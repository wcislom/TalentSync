---
paths:
  - "tests/**"
---

# Testing

## Projects

| Project | Kind | Covers |
|---|---|---|
| `TalentSync.Matching.Domain.Tests` | Unit, no I/O | Suggestion validation, `ConsultantProfile` behaviour |
| `TalentSync.Matching.Infrastructure.Tests` | Unit, no network | Matching Platform client: request body, status mapping (4xx → Rejected), HTTP pipeline. CV masking. Enrichment adapter over `FakeChatClient`: outcome per mode, timeout, open circuit, shutdown |
| `TalentSync.Sync.Application.Tests`, `TalentSync.Matching.Application.Tests` | Unit, no I/O | Use cases: each fetch result and port result → use case result. Ports replaced by small hand-written stubs |
| `TalentSync.Infrastructure.Teamtailor.Tests` | Unit, no network | Signature v2, webhook parsing, stage mapping, response mapping (404 → Deleted, 4xx → Rejected). Internals via `InternalsVisibleTo` |
| `TalentSync.Infrastructure.Messaging.Tests` | Unit, no I/O | Consumer skeleton: inbox and parking lot mapping, failure classification, handler loop |
| `TalentSync.Infrastructure.Observability.Tests` | Unit, no network | `AddObservability` with the SDK enabled on a test server (the request span is a root), `SpanExportPolicy` in front of an in-memory exporter. Internals via `InternalsVisibleTo` |
| `TalentSync.Sync.Infrastructure.Tests` | Unit, no network | CRM client: status mapping (404 on delete = done, 4xx → Rejected), HTTP pipeline. Internals via `InternalsVisibleTo` |
| `TalentSync.Matching.Evaluation.Tests` | Unit, no network | Prompt evaluation (ADR 009): metric calculations, golden set hash, case file validation, report format, the CI guard for the evaluation report. Never calls a model |
| `TalentSync.IntegrationTests` | Acceptance | Scenarios from `docs/acceptance-scenarios.md` |

## Acceptance tests

- One test per automated scenario. Method name: `<ID>_<ScenarioNameInPascalCase>`,
  e.g. `S2_DuplicateWebhookProducesOneCrmCall`. A Scenario Outline is one `[Theory]` with its Examples as data.
  `@manual` scenarios have no test.
- The test asserts exactly the scenario's Then, never something weaker. If the Then cannot be asserted as written,
  stop and say so; never edit the scenario to fit the test.
- Assert at the boundary: simulators' `GET /calls`, `/contacts`, `/profiles`, Ingress responses, and our own tables.
  Never assert on mocks of our own classes in integration tests.
- Each scenario uses its own candidate ID (e.g. `c-s2`); `/calls` is counted per candidate, so no reset is needed.
- LLM: `FakeChatClient` only. Tests never call a real model.

## Waiting for asynchronous effects

- Positive assertions poll with a timeout (default 30 s, poll every 500 ms) through one shared helper.
  Never a fixed `Task.Delay` / `Thread.Sleep`.
- Absence assertions ("no PUT", "no profile") first wait until the event has been processed, then assert:
  - Handled event types: wait until the worker's `Inbox` contains the event's `EventId`.
  - Skipped event types (e.g. `candidate.*` in Matching, scenario M8) write no inbox row. Use an **ordering marker**:
    after the event under test, send a handled event for the same candidate (same partition, so it is processed
    after the first one), wait for the marker in the `Inbox`, then assert.

## How the system under test runs

- Hosts run in process (`WebApplicationFactory<Program>`; each host has `public partial class Program;`).
  Infrastructure runs in Testcontainers: SQL Server (`Testcontainers.MsSql`) and the Event Hubs emulator with its
  Azurite (`Testcontainers.EventHubs`). The docker compose stack is for running the system by hand, not for tests.
- Containers start once per xUnit collection fixture (`tests/TalentSync.IntegrationTests/Infrastructure/`).
  The fixture applies migrations before the host starts.
- The emulator configuration in the fixture must match `deploy/eventhubs/Config.json`.
- Event Hubs outage: pause the emulator container (`PauseAsync`), never stop it. A restarted container gets a new
  host port and loses its events. The fixture shortens the SDK's send timeout so a send to a paused hub fails fast.
- Simulators run in process too (`WebApplicationFactory`). Every web host has a public global `Program` (.NET 10),
  so the test project references the workers and simulators with `Aliases`. Hosts reach each other through
  `HostRouter`, installed as the primary handler of every `HttpClient` (`ingress.test`, `teamtailor.test`,
  `crm.test`), so the clients' resilience pipelines run unchanged. All scenarios share one `SystemFixture`
  (`SystemCollection`).
- `FakeChatClient` calls: the fixture reads the `FakeChatClient` singleton from Matching.Worker's services
  (`InternalsVisibleTo TalentSync.IntegrationTests`) and counts the calls whose CV text contains the candidate's tag
  (`SystemFixture.CvTag`, e.g. `(ref c-m5)`). The test writes the tag into the CV; the model never receives our
  candidate id. The LLM mode is a `[llm:<Mode>]` marker in the same CV, never a switch on the shared host.
  The fixture sets `Llm:Timeout` to 2 s for M3. The circuit breaker is shared by all scenarios; only M3 fails a call,
  which stays below the configured failure ratio.

## Traces

Scenario O1 records spans with `SpanRecorder` (`Infrastructure/`), an `ActivityListener` on `TalentSync.*` and
`Microsoft.AspNetCore` (request spans): all hosts run in the test process, so no exporter is needed. Our spans are
matched by the `talentsync.event_id` tag, the request span by the webhook span's parent id. O1 runs without the SDK;
the SDK's own handling of the request span is covered by `EdgeTraceTests` in `TalentSync.Infrastructure.Observability.Tests`.

## Prompt evaluation guard

`EvaluationReportGuardTests` fails when `eval/reports/` has no report for the current `PromptVersion` and golden set
from a real model (ADR 009). A prompt or golden-set change needs a new report in the same PR; never make the test
pass by editing a report header or the test. The evaluation itself never runs in tests or CI.

## Packages

Ask before adding any package, including test helpers such as `Microsoft.Extensions.TimeProvider.Testing`
(`FakeTimeProvider` for the signature replay window).
