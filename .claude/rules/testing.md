---
paths:
  - "tests/**"
---

# Testing

## Projects

| Project | Kind | Covers |
|---|---|---|
| `TalentSync.Matching.Domain.Tests` | Unit, no I/O | Suggestion validation, `ConsultantProfile` behaviour |
| `TalentSync.Matching.Infrastructure.Tests` | Unit, no network | Matching Platform client: request body, status mapping (4xx → Rejected), HTTP pipeline |
| `TalentSync.Sync.Application.Tests`, `TalentSync.Matching.Application.Tests` | Unit, no I/O | Use cases: each fetch result and port result → use case result. Ports replaced by small hand-written stubs |
| `TalentSync.Infrastructure.Teamtailor.Tests` | Unit, no network | Signature v2, webhook parsing, stage mapping, response mapping (404 → Deleted, 4xx → Rejected). Internals via `InternalsVisibleTo` |
| `TalentSync.Infrastructure.Messaging.Tests` | Unit, no I/O | Consumer skeleton: inbox and parking lot mapping, failure classification, handler loop |
| `TalentSync.Sync.Infrastructure.Tests` | Unit, no network | CRM client: status mapping (404 on delete = done, 4xx → Rejected), HTTP pipeline. Internals via `InternalsVisibleTo` |
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
- LLM: `FakeChatClient` only. Tests never call Gemini.

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
- How `FakeChatClient` call counts fit in is decided with the first Matching scenario.

## Packages

Ask before adding any package, including test helpers such as `Microsoft.Extensions.TimeProvider.Testing`
(`FakeTimeProvider` for the signature replay window).
