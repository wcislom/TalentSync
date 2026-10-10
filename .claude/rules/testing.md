---
paths:
  - "tests/**"
---

# Testing

## Projects

| Project | Kind | Covers |
|---|---|---|
| `TalentSync.Matching.Domain.Tests` | Unit, no I/O | Suggestion validation, `ConsultantProfile` behaviour |
| `TalentSync.Infrastructure.Teamtailor.Tests` | Unit, no network | Signature v2, webhook parsing, stage mapping, response mapping (404 → Deleted, 4xx → Rejected). Internals via `InternalsVisibleTo` |
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

## Open decision: how the system under test runs

Not decided yet. Options: the docker compose stack with all hosts and simulators running, or hosts and simulators
started in-process by the test (SQL via Testcontainers). The choice also decides how tests read `FakeChatClient`
call counts. In the first integration test step, propose an option with trade-offs and wait for my decision.

## Packages

Ask before adding any package, including test helpers such as `Microsoft.Extensions.TimeProvider.Testing`
(`FakeTimeProvider` for the signature replay window).
