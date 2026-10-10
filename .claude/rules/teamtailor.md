---
paths:
  - "src/Shared/TalentSync.Infrastructure.Teamtailor/**"
  - "src/Shared/TalentSync.Recruitment.Domain/**"
  - "src/Ingress/TalentSync.Ingress.Api/**"
  - "simulators/FakeTeamtailor/**"
  - "tests/TalentSync.Infrastructure.Teamtailor.Tests/**"
---

# Teamtailor: verified facts and our assumptions

Verified against the official docs on 2026-10-08:
- https://partner.teamtailor.com/company_webhooks
- https://docs.teamtailor.com/

Anything marked **ASSUMPTION** is not documented. FakeTeamtailor implements it that way; keep it isolated in the ACL.

## Webhooks

- Event names: `candidate.create|update|destroy`, `job.create|update|destroy`,
  `job_application.create|update|destroy`.
- `update` fires only when selected fields change (candidate: e.g. email, phone; application: e.g. `stage_id`, `rejected_at`).
- Body: `{ "payload": { "event_name", "data": { "id", "type", "attributes" } }, "signature" }`.
  Attribute keys are kebab-case. `destroy` events carry a reduced `data`.
- Job application attributes include `job_id`, `stage_name`, `rejected_at`, `updated_at`. **No candidate id is listed.**
  **ASSUMPTION:** the payload contains `data.relationships.candidate.data.id`. Ingress reads the partition key from there.
- No event id in the payload. We derive `EventId` from a hash of the raw body.
- HTTPS only. No timeout documented. Respond 2xx fast, process asynchronously.
- Redelivery: the changelog (2026-03-19) says failed deliveries are **not retried**; the best-practices section says
  to be prepared for retries. Design for both: persist before 200, dedupe downstream.

## Signature v2 (header `TT-Signature`)

1. Base64-decode the header → `t=<unix timestamp>,v2=<hex HMAC>`.
2. Signed string = `<timestamp>` + `"."` + **raw request body bytes** (never re-serialised JSON).
3. Expected = hex(HMAC-SHA256(signature key, signed string)).
4. Compare with `CryptographicOperations.FixedTimeEquals`.
5. Reject if `|now - t| > 5 minutes` (our choice; Teamtailor documents no window). Use `TimeProvider`.
6. Invalid → 401.

v1 signed only `data.id` and is deprecated. **v2 signs the whole body.**
We still use notify-then-fetch because webhooks can arrive out of order, payloads are partial (no CV),
and redelivery is unclear. The API is the source of truth.

Verification lives in `TalentSync.Infrastructure.Teamtailor`. FakeTeamtailor signs with its own, independent
implementation. Do not share this code with the simulator: a shared bug would make the test pass on both sides.

## REST API

- Base URL `https://api.teamtailor.com` (EU). JSON:API, kebab-case.
- Headers on every request: `Authorization: Token token=<key>`, `X-Api-Version: 20240904` (required).
- Endpoints used: `GET /v1/candidates/{id}`, `GET /v1/job-applications/{id}?include=stage`.
  **ASSUMPTION:** stage is available via `include=stage`; the stages endpoint path is not documented.
- Rate limit: 50 requests per 10 seconds, then 429. Headers `X-Rate-Limit-Limit`, `X-Rate-Limit-Remaining`,
  `X-Rate-Limit-Reset` (seconds until window reset). **No `Retry-After` is documented.**
  The limit is per API key and both workers use it; each handles its own 429 (no shared counter in the MVP).
  How the ACL waits and retries: `.claude/rules/resilience.md`.
- Pagination `page[size]` max 30 (not needed in the MVP).
- CV: real API exposes `original-resume` (file) and `resume-summary`. **ASSUMPTION:** our fake exposes the CV as
  plain text in attribute `resume-text`. Documented as a simplification in the README.

## Port and ACL rules

- Port: `IRecruitmentSource` in `TalentSync.Recruitment.Domain`, named after the need, not the provider.
  Returns `Lookup<Candidate>` / `Lookup<JobApplication>` = `Found(T) | Deleted | Rejected(reason)`.
- Mapping of responses (normative):

| Response | Result |
|---|---|
| 2xx, mappable | `Found(T)` |
| 404 (**ASSUMPTION:** deleted resource) | `Deleted`, never an error |
| 400, other unexpected 4xx, or a 2xx body that cannot be mapped | `Rejected(reason)`: a permanent failure, parked (invariant 8, scenario S6) |
| 401, 403 (wrong key, missing header) | exception, transient: affects every event, so the partition waits for a config fix |
| 429, 5xx, timeout | exception, transient (see resilience rule) |

- `reason` holds status code and endpoint or field name only, never the response body (invariant 14).
- Adapter: `TeamtailorRecruitmentSource` in `TalentSync.Infrastructure.Teamtailor`, `internal`.
- Public surface of the project: see "Dependency rules" in `docs/architecture.md` (the only place that defines it).
  Only hosts reference this project.
- Stage names are customer-defined. The ACL maps a stage to `ApplicationStatus` using configuration
  (`Teamtailor:QualifiedStageName`), not a hard-coded string. Renaming the stage in Teamtailor = changing config.
- The word "Teamtailor" never appears in `*.Domain` or `*.Application`.
