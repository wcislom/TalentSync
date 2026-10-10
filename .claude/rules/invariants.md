# Invariants: never violate these

These apply to `src/`. They do NOT apply to `simulators/`.
One sentence each; the rule file in brackets holds the details. Acceptance scenarios reference these numbers.

## Delivery and idempotency

1. **Delivery is at-least-once everywhere.** Any event can arrive twice: from the outbox relay, from a partition
   restarting at the last checkpoint, or from Teamtailor itself.
2. **Correctness comes from idempotent effects, not from the inbox.** Every external effect is an upsert or delete
   keyed by `candidateId`, so repeating it creates no second record.
3. **The inbox is a skip-optimisation, written after the effect, never before.** An HTTP effect cannot share a
   SQL transaction with the inbox; marking first and crashing before the effect would lose the event. [messaging.md]
4. **Each worker owns its schema** and never reads or writes another one. A shared inbox would make the second
   worker skip every event as a duplicate. [persistence.md]
5. **Persist non-deterministic results before acting on them.** LLM output is stored by `(CandidateId, CvHash)`
   before the profile upsert, so a retry replays it instead of asking the model again. [llm.md]

## Event Hubs

6. **Checkpoint only after the event is handled:** effect done and inbox written, event parked, or event skipped
   (unhandled type, or already in the inbox).
7. **The processing handler never throws.** An exception escaping `ProcessEventAsync` faults the partition task
   or crashes the process.
8. **Transient failures wait, permanent failures are parked, retries loop inside the handler.** Transient
   (timeout, 5xx, 429, open circuit): retry in a loop inside `ProcessEventAsync`, no checkpoint, no parking;
   returning without a checkpoint does not redeliver the event. Permanent (a `Rejected` result: invalid data,
   mapping error, unexpected 4xx; or an envelope that cannot be read): write to the worker's `ParkingLot`, then checkpoint. Unclassified exceptions:
   parked after 5 attempts. [resilience.md, messaging.md]
9. `EventProcessorClient` and `EventHubProducerClient` are **singletons**.
10. Every published event has **partition key = `candidateId`**.
11. **Consumers are tolerant readers.** The envelope carries `SchemaVersion`; ignore unknown fields and event types.

## Data

12. **Uniqueness is guarded by unique indexes, never by check-then-insert.** A duplicate insert error
    (SqlException 2627/2601) means "already done". [persistence.md]
13. **One outbox relay replica in the MVP.** With several, `READPAST` can publish a later event of a candidate
    before an earlier one. [messaging.md]

## Security and privacy

14. **No PII in logs, traces, the outbox, Event Hubs or `ParkingLot.Error`.** The envelope carries IDs only.
    Log ids, never domain objects or DTOs (`ToString()` and `{@...}` destructuring print every property).
15. Send the LLM only the CV text, with email addresses and phone numbers masked. [llm.md]
16. **LLM output is untrusted input:** schema and domain validation, timeout, circuit breaker, fallback to
    `NotEnriched`. [llm.md]
17. **No secrets in the repo.** Locally: user-secrets or environment variables.
