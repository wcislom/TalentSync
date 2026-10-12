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
- Consumer groups are named after the consuming host: `sync-worker` (Sync.Worker), `matching-worker`
  (Matching.Worker). Each group has its own checkpoints in Blob storage (Azurite locally).
  The producer does not know who consumes.
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
- Implemented in `TalentSync.Ingress.Api/Outbox/`: `OutboxRelay` (loop, batch, marking), `OutboxPublisher` (one send),
  `OutboxQueries` (the locking query). Tunables in `OutboxRelay:*`. A failed batch is retried by a `Polly.Core`
  retry pipeline (exponential from `PollInterval`, capped at `MaxBackoff`, never gives up).
- The first failed send ends the batch: rows sent so far are marked and committed, the rest wait for the next
  batch. Continuing would publish a later event of a candidate before an earlier one.
- `READPAST` also skips a row whose insert is not committed yet, so rows can be published out of `Id` order.
  Harmless with notify-then-fetch (ADR 003).

## Consumer handler skeleton (`TalentSync.Infrastructure.Messaging`, used by both workers)

Implemented once and reused. Each worker plugs in its own handler for the event types it cares about
(Sync: `candidate.*`; Matching: `job_application.*`) and its own `DbContext` schema. The worker's handler runs the
use case and translates its result into `HandlerResult` (`Done` | `Permanent(reason)`); exceptions are classified
by the skeleton (`.claude/rules/resilience.md`).

Implemented in `TalentSync.Infrastructure.Messaging` (internal classes); they are the reference for the steps, and
invariants 3, 6, 7, 8 still apply to them:

- `Handling/EventProcessingLoop`: one event from tolerant read to checkpoint (unreadable → park, unhandled type or
  already in the inbox → checkpoint, handler → park or inbox), with the retry loop, the unclassified retry limit and
  shutdown. Its input is `ReceivedEvent`, so it does not depend on the Event Hubs SDK.
- `Handling/FailureClassifier`: transient or unclassified (`.claude/rules/resilience.md`).
- `EventHubs/EventProcessorHost`: `EventProcessorClient` → loop, a DI scope per event; creates the checkpoint container.
- `Persistence/ConsumerStore<TDbContext>`: inbox and parking lot in the worker's own schema.

A worker sees only the public surface in the project root (`IEventHandler`, `HandlerResult`, `AddMessaging`) plus the
`Inbox` / `ParkingLot` entities and their configurations. It registers its `IDbContextFactory<TDbContext>` and calls
`AddMessaging<TDbContext, THandler>(configuration)`.

A checkpoint is an offset, so skipping an unhandled type is safe even without an immediate checkpoint: the next
checkpoint covers it. The same mechanism is why a failed event must never be left behind by returning: the next
checkpoint would cover it too. Checkpointing after every event is fine for the MVP.
