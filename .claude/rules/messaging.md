---
paths:
  - "src/Ingress/TalentSync.Ingress.Api/**"
  - "src/Sync/TalentSync.Sync.Worker/**"
  - "src/Matching/TalentSync.Matching.Worker/**"
  - "src/Shared/TalentSync.Contracts/**"
  - "src/Shared/TalentSync.Infrastructure.Messaging/**"
  - "deploy/**"
---

# Messaging: outbox, Event Hubs, consumers

## Topology

- One hub: `ats-events`, 4 partitions, partition key = `candidateId`.
- Consumer groups: `crm-sync` (Sync.Worker), `matching` (Matching.Worker). Each group has its own checkpoints
  in Blob storage (Azurite locally). The producer does not know who consumes.
- Never a hub per worker: the producer would have to publish twice.
- Hub name and consumer group names must match between the emulator `Config.json` and `appsettings`.

## Event envelope (`TalentSync.Contracts`)

| Field | Meaning |
|---|---|
| `EventId` | SHA-256 of the raw webhook body, hex. Identical redelivered webhook = same id |
| `Source` | `"teamtailor"` (a value on the wire, part of the inbox key; not a code dependency) |
| `Type` | Teamtailor event name, e.g. `job_application.update` |
| `SchemaVersion` | Starts at 1 |
| `ResourceType`, `ResourceId` | From `payload.data` |
| `CandidateId` | Partition key. For job application events taken from the payload (see teamtailor rule) |
| `ReceivedAt` | From `TimeProvider` at ingress |

No attributes, no payload body: the envelope is a notification. Workers fetch current state from the API
(notify-then-fetch). `Contracts` references nothing.

## Ingress.Api

1. Read the raw body (enable buffering). Verify signature v2 through the verifier interface from
   `Infrastructure.Teamtailor` (see teamtailor rule). Invalid → 401.
   MVP: signature verification is done last; until then the endpoint skips it.
2. Parse the webhook through the parser interface from `Infrastructure.Teamtailor`, build the envelope,
   insert into `ingress.Outbox` in one local transaction, return 200.
3. If the insert fails → 503. Teamtailor may not retry; this loss is accepted (see known limitations).
4. Ingress does not call the Teamtailor API, the CRM or the Matching Platform.

## Outbox relay

- `BackgroundService` inside Ingress.Api. Loop: select unpublished rows
  `WITH (UPDLOCK, READPAST, ROWLOCK)` ordered by `Id`, publish with partition key, mark published, commit.
- Crash after publish and before mark → the event is published again. Expected; consumers handle it.
- One replica (invariant 13).

## Consumer handler skeleton (`TalentSync.Infrastructure.Messaging`, used by both workers)

Implemented once and reused. Each worker plugs in its own handler for the event types it cares about
(Sync: `candidate.*`; Matching: `job_application.*`) and its own `DbContext` schema. The worker's handler runs the
use case and translates its result into `HandlerResult` (`Done` | `Permanent(reason)`); exceptions are classified
by the skeleton (`.claude/rules/resilience.md`).

This pseudocode is a specification for the first implementation. Once the shared handler exists, replace this
section with a pointer to the class; from then on the class is the reference for the steps, and invariants
3, 6, 7, 8 still apply to it.

```
ProcessEventAsync(args):
  unclassifiedAttempts = 0
  loop:                                         # retries stay inside this call; returning moves on to the next event
    try:
      envelope = tolerant read
        unreadable (malformed JSON, missing required field) →
          insert ParkingLot row (partition + sequence number, raw body ≤ 4 KB), checkpoint, return
        unknown or unhandled type → checkpoint, return
      if inbox contains (Source, EventId) → checkpoint, return
      result = worker handler (runs the use case)
      if result is Permanent(reason):
        insert ParkingLot row (envelope + reason, no PII), checkpoint, return
      insert inbox row (2627/2601 → ignore)
      checkpoint, return
    catch OperationCanceledException when args.CancellationToken is cancelled:
      return                                    # shutdown: no checkpoint, no parking, no error log
    catch exception classified as transient:
      log ids only, back off, continue loop
    catch any other exception:
      unclassifiedAttempts += 1
      if unclassifiedAttempts < 5: log ids only, back off, continue loop
      insert ParkingLot row ("retry limit exceeded: <exception type>"), checkpoint, return
      # if this insert throws, the loop goes on: never checkpoint an event that was neither handled nor parked
  never rethrow
```

A checkpoint is an offset, so skipping an unhandled type is safe even without an immediate checkpoint: the next
checkpoint covers it. The same mechanism is why a failed event must never be left behind by returning: the next
checkpoint would cover it too. Checkpointing after every event is fine for the MVP.
