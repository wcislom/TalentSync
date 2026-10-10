---
paths:
  - "src/Shared/TalentSync.Infrastructure.Teamtailor/**"
  - "src/Shared/TalentSync.Infrastructure.Messaging/**"
  - "src/Sync/**"
  - "src/Matching/**"
---

# Resilience: HTTP clients, retries, failure classification

Normative: the split of responsibilities and the classification. Descriptive: retry counts and timeouts.

## Two retry layers, one job each

| Layer | Where | Handles | Gives up after |
|---|---|---|---|
| HTTP pipeline (`Microsoft.Extensions.Http.Resilience`) | each typed `HttpClient` | short blips: attempt timeout, a few quick retries, circuit breaker | seconds |
| Handler loop | `Infrastructure.Messaging` skeleton | long outages: back off, no checkpoint, the partition waits (invariant 8) | never for classified transient failures; see unclassified below |

- Retries multiply: every handler attempt runs the whole pipeline again. Keep the pipeline short; the handler loop
  is the layer that waits out an outage.
- **The retry is a loop inside `ProcessEventAsync`.** Returning from the handler without a checkpoint does not
  redeliver the event: the processor moves on to the next one, and the next checkpoint covers the failed event,
  so it is lost. Never return to "retry later".
- Back-off delays use `TimeProvider` and the processor's cancellation token (exponential, capped at 30 s).

## Per dependency

- **CRM, Matching Platform**: pipeline with attempt timeout, at most 2 retries, circuit breaker.
  When it gives up, the exception reaches the handler and is classified as transient.
- **Teamtailor API**: the generic pipeline must **not** retry 429. Teamtailor sends no `Retry-After`, so a generic
  retry would hit the API again inside the window (scenario S5 expects exactly 2 GETs). The ACL handles 429 itself:
  wait for `X-Rate-Limit-Reset` (via `TimeProvider`), retry once; a second 429 is thrown as transient.
  5xx and timeouts go through the pipeline as for the other clients.
  **UNCERTAIN:** whether the standard handler retries 429 by default. Check its predicate before relying on it.
- **LLM (`IChatClient`)**: timeout and circuit breaker, **no retry** (cost, free-tier limits). Any failure ends in
  `NotEnriched` inside the use case (D5); it never reaches the handler's classification.

## Permanent failures are values, transient failures are exceptions

A permanent failure is an expected outcome for this event ("this event cannot be processed"), so it is modelled as
a value the compiler forces you to handle. A transient failure is about the environment and arrives as an exception
from the HTTP stack anyway. This keeps the dependency rules intact: no shared exception type crosses projects.

| Who | Returns |
|---|---|
| `IRecruitmentSource` (ACL) | `Lookup<T>` = `Found(T)` \| `Deleted` \| `Rejected(reason)` |
| `ICrm`, `IMatchingPlatform` (adapters) | success, or `Rejected(reason)` for an unexpected 4xx |
| Use case (`*.Application`) | its own result type with a rejected case carrying the reason |
| Worker host | translates the use case result into `HandlerResult` (`Done` \| `Permanent(reason)`) from `Infrastructure.Messaging` |

- `reason` goes to `ParkingLot.Error`: status code and endpoint or field name, never response bodies or PII (invariant 14).
- Adapters never throw for a permanent failure, and `*.Application` never catches HTTP exceptions.

## Classification of exceptions (implemented once, in the skeleton)

- **Transient**: HTTP 5xx, 408, 429, 401, 403, connection failure, attempt timeout, open circuit,
  SQL connection or timeout errors. 401/403 means a wrong key or a missing header: it affects every event,
  so parking would only fill the parking lot.
- **Shutdown**: an `OperationCanceledException` while the processor's cancellation token is cancelled. Return:
  no checkpoint, no parking, no error log.
- **Unclassified** (anything else, e.g. a bug): retried with back-off, at most **5 attempts** counted in a local
  variable of the handler invocation, then parked with `retry limit exceeded: <exception type>` and checkpointed.
  If the parking insert itself fails (e.g. SQL is down), the loop keeps retrying: an event is never checkpointed
  without being handled or parked.
