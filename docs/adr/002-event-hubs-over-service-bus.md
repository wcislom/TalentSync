# ADR 002: Event Hubs over Service Bus

## Status

Accepted

## Context

Every change in Teamtailor must reach two independent consumers: Sync.Worker (CRM) and Matching.Worker
(Matching Platform). Each consumer must be able to fall behind, fail, or restart without affecting the other,
and it must be possible to read events again. Ingress should publish once and not know who consumes.

## Decision

Use Azure Event Hubs, through `Azure.Messaging.EventHubs` directly (`EventProcessorClient` with a Blob checkpoint
store; no MassTransit or other messaging abstraction).

- One hub, `ats-events`, partition key `candidateId`.
- One consumer group per worker: `crm-sync` (Sync.Worker) and `matching` (Matching.Worker). Each group has its own
  checkpoints in Blob storage (Azurite locally), so each worker has an independent read position in the same log.
- The producer (the outbox relay in Ingress.Api) publishes each event once and does not know who consumes it.
  A new consumer is a new consumer group, with no change to the producer.
- Events stay in the log after they are read, so a consumer can replay from an earlier position.

## Consequences

- Replay from the log and independent consumers come from the broker, not from our code.
- **Cost: there is no dead-letter queue.** Permanent failures (a `Rejected` result, an unreadable envelope) and
  unclassified exceptions after 5 attempts are written to a `ParkingLot` table in the worker's own schema
  (`sync.ParkingLot`, `matching.ParkingLot`), then checkpointed.
- **Cost: failure classification and retries are our code.** Returning from the handler without a checkpoint does
  not redeliver the event, so transient failures are retried in a loop inside `ProcessEventAsync`.
  This logic is implemented once in `TalentSync.Infrastructure.Messaging`.
- Checkpoints are our responsibility: only after the event is handled, never before. Checkpointing late means
  events are processed again after a restart, so delivery is at-least-once and effects must be idempotent
  (see `.claude/rules/invariants.md`).
- Ordering is guaranteed only within a partition. Events of one candidate share a partition because the partition
  key is `candidateId`.
- Replaying parked events needs a replay endpoint or job, which is out of MVP scope.
- Local development needs Azurite for checkpoints and partition ownership, next to the Event Hubs emulator.

## Alternatives considered

- **Service Bus topics and subscriptions.** Its built-in dead-letter queue would make the parking lot
  unnecessary. Rejected: the author's production experience is with Service Bus; this project deliberately uses
  Event Hubs instead.
- **One hub per worker.** Rejected: the producer would have to publish every event twice (a dual write), and every
  new consumer would require a change to the producer.
