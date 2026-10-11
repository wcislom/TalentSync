# ADR 006: Partition key = candidateId

## Status

Proposed

## Context

Event Hubs keeps order only within a partition. Within one consumer group, a partition is read by one processor at a
time, one event after another; different partitions are processed in parallel.

All effects are keyed by `candidateId` (ADR 005), and one candidate produces events of two kinds: `candidate.*` and
`job_application.*`, possibly for several applications (D3). Without a shared partition, two events of the same
candidate could be processed at the same time, for example two qualified applications upserting the same profile,
or a `candidate.update` racing a `candidate.destroy`.

Teamtailor documents no candidate id in job application webhooks. The listed attributes are `job_id`, `stage_name`,
`rejected_at` and `updated_at`.

## Decision

Every published event uses `candidateId` as its partition key. Event Hubs hashes the key to a partition; we never
choose partition ids.

- For `candidate.*` the key is `payload.data.id`.
- For `job_application.*` the key is `payload.data.relationships.candidate.data.id`. This is an **ASSUMPTION**
  (`.claude/rules/teamtailor.md`), kept in the webhook parser in `Infrastructure.Teamtailor`.
- A webhook without a candidate id is not routable. Ingress answers 400 and logs the reason; it does not guess a key
  and does not call the API to find one (Ingress calls no APIs, ADR 004).
- The hub `ats-events` has 4 partitions locally.

## Consequences

- Events of one candidate are processed in order and one at a time in each worker, so no two effects on the same
  `candidateId` run concurrently. The exception is the short window during a change of partition ownership
  (ADR 005).
- **Cost: parallelism is capped by the partition count.** Each consumer group has at most one active processor
  per partition, so replicas of a worker beyond the partition count stay idle. The count is set when the hub is
  created; changing it later depends on the tier and remaps keys to partitions, which breaks ordering for events
  already in the hub.
- **Cost: head-of-line blocking.** A transient failure (CRM outage, 429, wrong API key) is retried inside the handler,
  so every other candidate on that partition waits too. Permanent failures are parked and do not block (ADR 002).
- **Cost: the job application payload is an assumption.** If real Teamtailor webhooks do not carry
  `relationships.candidate`, every `job_application.*` webhook is rejected with 400 and Matching.Worker receives
  nothing. This must be checked against a real webhook before production. `destroy` events carry a reduced `data`
  and may be rejected the same way; Matching.Worker does not act on them in the MVP (D4).
- Ordering still depends on the producer: one relay replica (ADR 004). Events of one candidate that are published
  out of order are harmless with notify-then-fetch (ADR 003).

## Alternatives considered

- **Job application id as the key for `job_application.*`.** Rejected: two applications of one candidate would
  land on different partitions and could upsert the same profile concurrently, and there would be no order between
  a candidate's application events and its candidate events.
- **No partition key (round-robin).** Rejected: no ordering at all, and events of one candidate would run
  concurrently on different partitions.
- **Ingress fetches the application from the API to find the candidate id.** Rejected: Ingress would depend on the
  Teamtailor API and its rate limit before answering the webhook.
