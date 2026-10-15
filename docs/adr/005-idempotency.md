# ADR 005: Idempotency

## Status

Accepted

## Context

Any event can reach a worker more than once:

- Teamtailor may deliver the same webhook again (the docs disagree on whether it retries, ADR 003).
- The outbox relay publishes again after a crash between the send and marking the row (ADR 004).
- A partition restarts from its last checkpoint after a crash, a restart or a change of partition ownership, so
  events after that checkpoint are processed again (ADR 002).

The effects are HTTP calls to the CRM and the Matching Platform. They cannot share a transaction with SQL Server or
with an Event Hubs checkpoint, so "effect done" and "event marked as done" can never be one atomic step: a crash
can always fall between them. Exactly-once processing is not available; at-least-once is.

Two workers consume the same events, each for its own effect. Matching.Worker also calls an LLM, whose answer
differs from call to call.

## Decision

Correctness comes from the effects themselves; everything else only saves work.

- **Idempotent effects keyed by `candidateId`.** Every external effect is an upsert or a delete addressed by the
  Teamtailor candidate id: `PUT /contacts/{candidateId}`, `DELETE /contacts/{candidateId}` (a contact that does not
  exist counts as already deleted, D1), `PUT /profiles/{candidateId}`. Repeating any of them leaves the same
  state. Combined with notify-then-fetch (ADR 003), a repeated event sends the current state again, never an old one.
- **Per-worker inbox, written after the effect.** Each worker has an `Inbox` table in its own schema
  (`sync.Inbox`, `matching.Inbox`) with a unique index on `(Source, EventId)`. A handled event is inserted after its
  effect succeeded; an event already in the inbox is skipped. A duplicate key error on insert (SqlException 2627 or
  2601) means "already done". The inbox is a skip-optimisation, not the guarantee.
- **Checkpoint after handling.** A worker checkpoints only once the event is handled: effect done and inbox row
  written, event parked, or event skipped.
- **Stored LLM results.** A valid enrichment is stored in `matching.EnrichmentResults` under a unique
  `(CandidateId, CvHash, PromptVersion)` before the profile upsert. A retry finds it and replays the same suggestion instead of
  asking the model again. A failed enrichment is not stored.
- `EventId` is the SHA-256 of the raw webhook body, so a redelivered identical webhook has the same id.

## Consequences

- **A crash between the effect and the inbox insert repeats the effect.** The event is processed again and sends
  a second `PUT`. This is correct behaviour, made safe by the upsert; it is why crash recovery is asserted on
  records, not on calls (scenario S7).
- Exact duplicates within the same worker cost one fetch and no effect, as long as the first one reached the inbox
  (scenario S2). Two events with different bodies about the same change (for example a webhook that Teamtailor
  sends again with a new timestamp) have different `EventId`s, so the inbox does not catch them; the idempotent
  effect does.
- With several replicas of a worker, a change of partition ownership can let two processors briefly handle the
  same event. Both perform the effect; the second inbox insert fails with a duplicate key and is treated as done.
  **Cost:** in that window the slower one can write an older state of the candidate after the newer one. The next
  event of that candidate corrects it. With one replica per worker this window does not occur.
- The two workers never see each other's inbox, so neither skips an event the other has handled.
- **Cost: the downstream APIs must offer an operation keyed by our id.** The simulators do. A real CRM without an
  upsert by external id would need a lookup by an external-id field first, and that lookup and the create are not
  atomic; this is not modelled.
- **Cost: the stored enrichment is tied to the CV text.** A changed CV, including a change only to an email
  address or phone number in it, gives a new hash and a new model call.
- **Cost: the inbox grows without limit.** No retention or clean-up job in the MVP.

## Alternatives considered

- **Mark the event in the inbox before the effect.** Rejected: a crash after marking and before the effect would
  skip the event on redelivery, and the change would be lost.
- **Rely on the inbox alone, with effects that create records.** Rejected: a crash between the effect and the inbox
  insert would create a second record, and duplicates with a different body would pass the inbox anyway.
- **One inbox shared by both workers.** Rejected: both workers receive every event, and the inbox key does not say
  who handled it, so once both handle the same event type the first one would make the other skip it. It would also
  put one table between two deployables, against schema ownership.
- **A distributed transaction across the effect, the inbox and the checkpoint.** Not possible: HTTP calls to
  third-party APIs and Event Hubs checkpoints cannot enlist in a SQL transaction.
- **Calling the LLM again on every retry.** Rejected: the model may answer differently, so a retry after a crash
  could overwrite the profile with a different suggestion, and each retry costs a model call.
