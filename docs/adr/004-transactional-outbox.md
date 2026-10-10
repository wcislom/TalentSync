# ADR 004: Transactional outbox in Ingress

## Status

Accepted

## Context

Teamtailor expects a fast 2xx and documents no timeout. Its changelog says failed deliveries are not retried, so an
event that is not persisted before Ingress answers 200 may be lost for good. Ingress must not call the Teamtailor
API, the CRM or the Matching Platform; its only job is to accept the webhook and hand it on to Event Hubs.

## Decision

Ingress persists the event before answering, and a separate relay publishes it.

- The webhook endpoint verifies the signature, parses the webhook, builds the envelope (IDs only) and inserts it
  into `ingress.Outbox` in one local transaction, then returns 200. If the insert fails, it returns 503.
- The outbox relay is a `BackgroundService` inside Ingress.Api. In a loop it selects unpublished rows
  `WITH (UPDLOCK, READPAST, ROWLOCK)` ordered by `Id`, publishes each with partition key `candidateId` through a
  singleton `EventHubProducerClient`, marks it published and commits.
- One relay replica in the MVP.

## Consequences

- The webhook response depends only on SQL Server, not on Event Hubs: publishing happens later, in the relay.
- **A crash after publish and before the row is marked publishes the event again.** This is expected: delivery is
  at-least-once, and consumers handle duplicates.
- **Cost: a known gap.** If SQL Server is down at ingress, the webhook gets 503 and Teamtailor may not retry: the
  event is lost. A reconciliation job against the API would close this gap; it is out of MVP scope.
- **Cost: one relay replica only.** With several, `READPAST` could publish a later event of a candidate before an
  earlier one.
- **Cost: more moving parts** in Ingress: an outbox table and a background loop, and a delay between accepting the
  webhook and publishing it.
- The outbox holds the envelope only, so it contains no PII.

## Alternatives considered

- **Publishing to Event Hubs directly from the webhook endpoint.** Rejected: the webhook would then depend on Event
  Hubs being available, and an event that fails to publish is answered with an error that Teamtailor may not retry.
  TODO: confirm this rationale.
