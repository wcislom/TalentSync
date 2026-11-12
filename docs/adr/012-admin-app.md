# ADR 012: Admin app

## Status

Accepted

## Context

Parked events can only be read with a SQL client, one schema per worker. The demo scenarios are driven by hand from
`docs/e2e.http`, which needs an IDE and reads response bodies to follow ids between requests. We want one place
to run a scenario and see what was parked, without opening SQL.

## Decision

- **An Angular single-page app, `src/Admin/TalentSync.Admin.Web`.** Placed by context first, then by deployable kind,
  like `src/Ingress/TalentSync.Ingress.Api`. Built in CI, its own image, deployed with the same Helm chart and
  released together with the hosts.
- **Two pages.** *Parking lot*: a read-only list and detail of parked events from both workers, merged in the
  browser. *Scenarios*: buttons that drive the demo scenarios against the simulators. It replaces `docs/e2e.http`,
  which is deleted, not kept as a copy.
- **A read-only admin endpoint in each worker.** Each worker returns only its own `ParkingLot` (invariant 4).
  No replay, no write endpoints.
- **No authentication in the MVP.** The app and the endpoints run only locally and in the local cluster. Adding it
  later (OIDC in the app, JWT bearer in the workers) is a separate decision.
- **How the app reaches the APIs.** Locally the browser calls the workers and the simulators directly, so they
  allow the app's origin (CORS). In the cluster an nginx container serves the app and reverse-proxies to the worker
  Services, which stay unexposed. API addresses come from a `config.json` read at runtime (a ConfigMap in the
  cluster), not from `environment.ts`, which is fixed at build time.
- **OpenAPI is the contract.** Each worker writes its OpenAPI document to a committed file at build time
  (`Microsoft.AspNetCore.OpenApi`, `Microsoft.Extensions.ApiDescription.Server`). The app's TypeScript types are
  generated from those files with `openapi-typescript` (types only; HTTP calls stay hand-written) and committed too.
  CI regenerates both and fails on any difference.
- **Neutral names in the app.** The Teamtailor simulator is the "ATS simulator" in app code: the name
  "Teamtailor" is restricted inside `src/` (CLAUDE.md, Naming).

## Consequences

- The workers serve HTTP endpoints without authentication. Acceptable only because nothing runs outside a
  developer's machine; any shared deployment needs authentication first.
- The admin endpoint returns `ParkingLot.Error` and the envelope as stored. Both are PII-free by invariant 14,
  so the endpoint adds no new exposure; a change that put PII there would now also reach the browser.
- The parking lot holds no candidate id or event type column: both are read from the stored envelope and are
  missing for an envelope that could not be read.
- A Scenarios page that triggers the poison case parks one event in each worker, since the simulator's
  `bad-request` anomaly applies to the candidate and to that candidate's job applications.
- CI needs Node and `npm ci`. App tests cover only the parking lot calls: the merge of both workers' lists and a
  failing worker (the other worker's rows still show, with a notice). The Scenarios page has no tests: it only
  fires requests at the simulators, and the behaviour it triggers is covered by the integration tests.
- The ADR 011 rule "only our hosts run in the cluster" now includes the Admin app; ADR 011 is amended when the app
  is added to the chart.

## Alternatives considered

- **`ui/` at the repository root.** Rejected: `src/` is what we build and ship, and the app is one of those.
- **A separate repository.** Rejected: a contract change would need two PRs, and the chart and image would live in
  different repositories, while the deployables are released together.
- **One admin endpoint reading both parking lots.** Rejected: it would read another worker's schema (invariant 4).
- **Blazor WebAssembly.** One language with the backend. Rejected: the portfolio should show a mainstream SPA stack.
- **A client generator (`openapi-generator`, `ng-openapi-gen`).** Rejected: it generates HTTP services we would
  not read, and `openapi-generator` needs Java. Types are enough.
- **Hand-written TypeScript types with a .NET test of the JSON shape.** No new packages. Rejected: the test and the
  types drift separately, and nothing stops a field rename from reaching the app.
- **Keep `docs/e2e.http` next to the Scenarios page.** Rejected: two copies of the same steps drift apart.
