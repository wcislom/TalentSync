# Simulators

Fake external systems used to run and demo TalentSync locally. **They are not part of the product.**

- Minimal API, one `Program.cs` plus a few small files. Data in memory (`ConcurrentDictionary`). No database.
- No Clean Architecture, no inbox/outbox, no resilience. The invariants in `.claude/rules/invariants.md` do not apply here.
- Seed data is hard-coded and clearly fake (invented names, `example.com` emails).
- No references to `src/` projects. Simulators speak HTTP only.

## Test endpoints (the same on all three, at the root)

- `POST /chaos` switches a failure mode for N seconds, optionally for one candidate ID only.
  Each simulator lists its modes below. Simulators must be **easy to break on purpose**.
- `GET /calls`: counts per endpoint, per candidate and per response status code,
  so tests can prove "one effect" (see `docs/acceptance-scenarios.md`).

## FakeTeamtailor

Two separate surfaces, plus the test endpoints above:

| Surface | Endpoints | Purpose |
|---|---|---|
| Scenario driving `/sim` (not Teamtailor API) | `GET /sim/jobs`, `POST /sim/jobs/{id}/apply` (name, email, phone, `cv.txt`), `PATCH /sim/job-applications/{id}/stage`, `DELETE /sim/candidates/{id}` | Drive the recruitment scenario from `.http` files and tests |
| Fake API `/v1` | `GET /v1/candidates/{id}`, `GET /v1/job-applications/{id}?include=stage` | What the TalentSync ACL calls. JSON:API, kebab-case |

- Every state change sends a webhook to Ingress (`candidate.create`, `job_application.update`, ...),
  shaped as in `.claude/rules/teamtailor.md`, including `relationships.candidate` on job application events.
- Webhooks are signed with signature v2 using `FakeTeamtailor:SignatureKey` (must equal `Teamtailor:SignatureKey`
  in Ingress). Own implementation, never shared with `src/`.
- Chaos modes:
  - `rate-limit`: `/v1` returns 429 with `X-Rate-Limit-*` headers; `X-Rate-Limit-Reset` = seconds until the mode ends.
  - `bad-request`: `/v1` returns 400 (a permanent failure, scenario S6).
  - `delay`, `duplicate-webhook` (send the same body N times, default 2), `bad-signature`, `stale-timestamp`.
- `/v1` requires `Authorization: Token token=...` and `X-Api-Version`; reject without them (400/401) so the ACL is tested.
- Stages: `New`, `Screening`, `Interview`, `Qualified`, `Rejected`.
- Deleted candidates return 404 from `/v1`.

## FakeCrm

- `PUT /contacts/{candidateId}` upsert, `GET /contacts` (each contact with its status `Active` / `Deleted`).
- `DELETE /contacts/{candidateId}` is a soft delete: the contact stays with status `Deleted`, 204.
  Already deleted → 204. Unknown contact → 404.
- Chaos modes: `503`, `timeout`.

## FakeMatchingPlatform

- `PUT /profiles/{candidateId}` upsert (skills, seniority, confidence, status `Enriched` / `NotEnriched`).
- `GET /profiles`, `GET /profiles/{candidateId}`: used in the demo to show the end result.
- Chaos modes: `503`, `timeout`.
