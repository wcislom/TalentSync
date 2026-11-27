# Testing

- **Acceptance scenarios** ([acceptance-scenarios.md](acceptance-scenarios.md)) are the definition of done:
  Given/When/Then examples of the domain rules and invariants, written before the code. Each automated scenario has
  exactly one integration test named after its ID, e.g. `S4` (CRM outage delays but does not lose events) or `M3`
  (LLM timeout yields a `NotEnriched` profile). The tests run the three hosts and the simulators in process against
  SQL Server, Azurite and the Event Hubs emulator in Testcontainers, and break the simulators on purpose.
- **Unit tests** per project: the domain model, use cases, failure classification in the consumer skeleton, HTTP
  clients and their resilience, the Teamtailor anti-corruption layer.
- **CI** (GitHub Actions) builds and runs everything on every PR, with a test report and a coverage summary.
- **Prompt evaluation** ([ADR 009](adr/009-prompt-evaluation.md)): the enrichment prompt is scored offline
  against a golden set of fictional CVs (`eval/cases/`, including a prompt injection and a Polish CV) for validity,
  recall, precision, hallucinations, seniority and stability. Run by hand, never in CI, because it calls the real
  model; reports are in [`eval/reports/`](../eval/reports/). CI only checks that a report exists for the current prompt.
