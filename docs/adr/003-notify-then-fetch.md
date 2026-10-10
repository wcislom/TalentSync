# ADR 003: Notify-then-fetch

## Status

Accepted

## Context

Teamtailor webhooks can arrive out of order, and their payloads are partial: job application events carry no CV,
and `destroy` events carry a reduced `data`. Redelivery is unclear: the changelog (2026-03-19) says failed
deliveries are not retried, while the best-practices section says to be prepared for retries. Webhook payloads
contain PII, which must not reach the outbox or Event Hubs (invariant 14).

## Decision

The webhook is treated as a signal, and the Teamtailor API as the source of truth.

- Ingress builds an envelope that carries IDs only: `EventId`, `Source`, `Type`, `SchemaVersion`, `ResourceType`,
  `ResourceId`, `CandidateId`, `ReceivedAt`. No attributes, no payload body.
- Each worker fetches the current state through `IRecruitmentSource` (`GET /v1/candidates/{id}`,
  `GET /v1/job-applications/{id}?include=stage`) and acts on that, not on the webhook.
- A 404 from the API maps to `Deleted`, never to an error.

This is **not** a workaround for an unsigned payload: signature v2 signs the whole body, and Ingress verifies it.

## Consequences

- Stale or reordered events are harmless: each worker acts on the current API state, whatever order the events
  arrive in.
- A redelivered or duplicated event triggers a fetch of the same current state, so it causes no wrong effect.
- The CV is available to Matching.Worker, which it would not be from the webhook payload.
- No PII in `ingress.Outbox` or Event Hubs: the envelope holds IDs only.
- **Cost: extra API calls.** Every handled event costs one or more GETs. The rate limit is 50 requests per
  10 seconds per API key, shared by both workers, each handling 429 on its own (no shared counter in the MVP).
  The ACL waits for `X-Rate-Limit-Reset` and retries once; a second 429 is a transient failure.
- **Cost: the workers depend on the Teamtailor API being up.** An outage is a transient failure: the partition
  waits until the API answers again.
- Ingress still reads one value from the payload: `CandidateId` for job application events, used as the partition
  key. Its location is an **ASSUMPTION** (`data.relationships.candidate.data.id`; see `.claude/rules/teamtailor.md`).

## Alternatives considered

- **Acting on the webhook payload.** Rejected: events can arrive out of order, so an older payload could overwrite
  newer data; payloads are partial (no CV); unclear redelivery makes it uncertain which payload is the latest;
  and the payload would carry PII into the outbox and Event Hubs.
