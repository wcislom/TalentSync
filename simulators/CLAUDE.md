# Simulators

Fake external systems used to run and demo TalentSync locally. **They are not part of the product.**

- Minimal API, one `Program.cs` plus a few small files. Data in memory (`ConcurrentDictionary`). No database.
- No Clean Architecture, no inbox/outbox, no resilience. The invariants in `.claude/rules/invariants.md` do not apply here.
- Code quality is not a goal: simulators are test tooling, never deployed. Working, readable and easy to break is
  enough. Do not refactor them for structure, and do not write tests for them; the integration tests exercise them.
- Seed data is hard-coded and clearly fake (invented names, `example.com` emails).
- No references to `src/` projects. Simulators speak HTTP only.

## Test endpoints (the same on all three, at the root)

- `POST /chaos` `{ "mode", "seconds"?, "candidateId"? }` switches a failure mode for N seconds (no `seconds` = until
  cleared), optionally for one candidate ID only. Several rules can be active at once; the same mode and candidate
  replaces the rule. `GET /chaos` lists active rules, `DELETE /chaos?candidateId=` clears them (all without the id).
  Tests always pass `candidateId`, so a failure mode never leaks into another scenario running in parallel.
  Each simulator lists its modes below. Simulators must be **easy to break on purpose**.
- `GET /calls`: counts per endpoint, per candidate and per response status code,
  so tests can prove "one effect" (see `docs/acceptance-scenarios.md`).

## FakeTeamtailor

Two separate surfaces, plus the test endpoints above:

| Surface | Endpoints | Purpose |
|---|---|---|
| Scenario driving `/sim` (not Teamtailor API) | `GET /sim/jobs`, `POST /sim/jobs/{id}/apply` (name, email, phone, `cv.txt`, optional `candidateId`), `PATCH /sim/job-applications/{id}/stage`, `PATCH /sim/candidates/{id}`, `DELETE /sim/candidates/{id}` | Drive the recruitment scenario from `.http` files and tests |
| Fake API `/v1` | `GET /v1/candidates/{id}`, `GET /v1/job-applications/{id}?include=stage` | What the TalentSync ACL calls. JSON:API, kebab-case |

- Every state change sends a webhook to Ingress (`candidate.create`, `job_application.update`, ...),
  shaped as in `.claude/rules/teamtailor.md`, including `relationships.candidate` on job application events
  (also on `destroy`, otherwise Ingress has no partition key).
  A state change that changes nothing (same stage, same values) sends no webhook, like Teamtailor.
- `apply` with an existing `candidateId` adds an application to that candidate (no `candidate.create`); without one
  a new candidate is created. Tests pass their own ids (`c-s1`, ...) so `/calls` counts never mix between scenarios.
- Webhooks are signed with signature v2 using `FakeTeamtailor:SignatureKey` (must equal `Teamtailor:SignatureKey`
  in Ingress). Own implementation, never shared with `src/`.
- Chaos modes:
  - `rate-limit` (needs `seconds`): `/v1` returns 429 with `X-Rate-Limit-*` headers; `X-Rate-Limit-Reset` = seconds
    until the mode ends, rounded up.
  - `bad-request`: `/v1` returns 400 (a permanent failure, scenario S6).
  - `delay` (needs `delayMs`): `/v1` answers after the delay. Webhooks are not delayed.
  - `duplicate-webhook` (`count`, default 2): each webhook of the candidate is sent `count` times with identical bytes.
  - The header check runs first: a wrong key still gets 401 while a chaos mode is on.
  - No signature modes: invalid and stale signatures are covered by scenarios I3 and I4, which call Ingress directly.
- `/v1` requires `Authorization: Token token=...` and `X-Api-Version`; reject without them (400/401) so the ACL is tested.
- Stages: `New`, `Screening`, `Interview`, `Qualified`, `Rejected`.
- Deleted candidates return 404 from `/v1`.
- `/calls` counts only `/v1` requests (what TalentSync calls), `?candidateId=` filters. A job application call is
  counted under its candidate.

## FakeCrm

- `PUT /contacts/{candidateId}` upsert, `GET /contacts` (each contact with its status `Active` / `Deleted`).
- `DELETE /contacts/{candidateId}` is a soft delete: the contact stays with status `Deleted`, 204.
  Already deleted → 204. Unknown contact → 404.
- Chaos modes: `503`, `timeout`.

## FakeMatchingPlatform

- `PUT /profiles/{candidateId}` upsert (skills, seniority, confidence, status `Enriched` / `NotEnriched`).
- `GET /profiles`, `GET /profiles/{candidateId}`: used in the demo to show the end result.
- Chaos modes: `503`, `timeout`.
