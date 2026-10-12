# Simulators

Fake external systems used to run and demo TalentSync locally. **They are not part of the product.**

- Minimal API: a short `Program.cs`, the other files in folders by who calls them, with the namespace following
  the folder: one named after the faked system for what TalentSync sees (e.g. `Teamtailor/`: API, webhooks, wire
  format), `Testing/` for what tests and `.http` files use (`/anomalies`, `/calls`, scenario driving), `State/`
  for the in-memory data both use. A file is named after its main type; a class with `Map...` is `<Feature>Endpoints`.
  Data in memory (`ConcurrentDictionary`). No database.
- No Clean Architecture, no inbox/outbox, no resilience. The invariants in `.claude/rules/invariants.md` do not apply here.
- Code quality is not a goal: simulators are test tooling, never deployed. Working, readable and easy to break is
  enough. Do not add layers or abstractions beyond those folders, and do not write tests for them; the integration tests exercise them.
- Seed data is hard-coded and clearly fake (invented names, `example.com` emails).
- No references to `src/` projects. Simulators speak HTTP only.

## Test endpoints (the same on all three, at the root)

- `POST /anomalies` `{ "mode", "seconds"?, "candidateId"? }` switches a failure mode for N seconds (no `seconds` = until
  cleared), optionally for one candidate ID only. Several rules can be active at once; the same mode and candidate
  replaces the rule. `GET /anomalies` lists active rules, `DELETE /anomalies?candidateId=` clears them (all without the id).
  Tests always pass `candidateId`, so a failure mode never leaks into another scenario running in parallel.
  Each simulator lists its modes below. Simulators must be **easy to break on purpose**.
- `GET /calls`: counts per endpoint, per candidate and per response status code,
  so tests can prove "one effect" (see `docs/acceptance-scenarios.md`).

## FakeTeamtailor

Two separate surfaces, plus the test endpoints above:

| Surface | Endpoints | Purpose |
|---|---|---|
| Recruiter `/recruiter` (not Teamtailor API) | `GET /recruiter/jobs`, `POST /recruiter/jobs/{id}/apply` (JSON: name, email, phone, `resumeText`, optional `candidateId`), `PATCH /recruiter/job-applications/{id}/stage`, `DELETE /recruiter/job-applications/{id}`, `PATCH /recruiter/candidates/{id}`, `DELETE /recruiter/candidates/{id}` | Drive the recruitment scenario from `.http` files and tests |
| Fake API `/v1` | `GET /v1/candidates/{id}`, `GET /v1/job-applications/{id}?include=stage` | What the TalentSync ACL calls. JSON:API, kebab-case |

- Every state change sends a webhook to Ingress (`candidate.create`, `job_application.update`, ...),
  shaped as in `.claude/rules/teamtailor.md`, including `relationships.candidate` on job application events
  (also on `destroy`, otherwise Ingress has no partition key).
  A state change that changes nothing (same stage, same values) sends no webhook, like Teamtailor.
- `apply` with an existing `candidateId` adds an application to that candidate (no `candidate.create`); without one
  a new candidate is created. Tests pass their own ids (`c-s1`, ...) so `/calls` counts never mix between scenarios.
- Webhooks are signed with signature v2 using `FakeTeamtailor:SignatureKey` (must equal `Teamtailor:SignatureKey`
  in Ingress). Own implementation, never shared with `src/`.
- Anomaly modes:
  - `rate-limit` (needs `seconds`): `/v1` returns 429 with `X-Rate-Limit-*` headers; `X-Rate-Limit-Reset` = seconds
    until the mode ends, rounded up.
  - `bad-request`: `/v1` returns 400 (a permanent failure, scenario S6).
  - `delay` (needs `delayMs`): `/v1` answers after the delay. Webhooks are not delayed.
  - `duplicate-webhook` (`count`, default 2): each webhook of the candidate is sent `count` times with identical bytes.
  - The header check runs first: a wrong key still gets 401 while an anomaly mode is on.
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
- `PUT` body: `firstName`, `lastName`, `email` (required, else 400), `phone`. 201 on create, 200 on update.
  A `PUT` on a deleted contact makes it `Active` again.
- Starts empty: every contact comes from Sync.Worker (scenario S8 needs a candidate without one).
- Anomaly modes:
  - `503`: `PUT` / `DELETE` return 503 without an effect.
  - `timeout`: the effect is applied, then the response hangs until the client gives up (recorded as 499 in `/calls`).
- `/calls` counts only `PUT` / `DELETE /contacts/{candidateId}`. `GET /contacts` is not counted and never broken.

## FakeMatchingPlatform

- `PUT /profiles/{candidateId}` upsert (skills, seniority, confidence, status `Enriched` / `NotEnriched`).
  201 on create, 200 on update, last write wins. 400 without `status`, with an unknown `status` or `seniority`,
  with `Enriched` but no `seniority` or `confidence`, or with `confidence` outside 0–1.
- `GET /profiles`, `GET /profiles/{candidateId}` (404 if none): the end result for tests and the demo.- No `DELETE`: archiving profiles is out of MVP. Starts empty (scenario M2 needs a candidate without a profile).
- Anomaly modes, same as FakeCrm:
  - `503`: `PUT` returns 503 without an effect.
  - `timeout`: the effect is applied, then the response hangs until the client gives up (recorded as 499 in `/calls`).
- `/calls` counts only `PUT /profiles/{candidateId}`. The `GET`s are not counted and never broken.
