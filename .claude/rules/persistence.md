---
paths:
  - "src/Ingress/TalentSync.Ingress.Api/**"
  - "src/Shared/TalentSync.Infrastructure.Messaging/**"
  - "src/Sync/TalentSync.Sync.Infrastructure/**"
  - "src/Matching/TalentSync.Matching.Infrastructure/**"
  - "**/Migrations/**"
  - "**/*DbContext*.cs"
---

# Persistence (SQL Server 2025, EF Core)

## Schemas and ownership

| Schema | Owner | `DbContext` lives in | Tables |
|---|---|---|---|
| `ingress` | Ingress.Api | `TalentSync.Ingress.Api` | `Outbox` |
| `sync` | Sync.Worker | `TalentSync.Sync.Infrastructure` | `Inbox`, `ParkingLot` |
| `matching` | Matching.Worker | `TalentSync.Matching.Infrastructure` | `Inbox`, `ParkingLot`, `EnrichmentResults` |

One `DbContext` per owner, each mapped to its own schema with its own migrations history table
(`<schema>.__EFMigrationsHistory`). A host registers only its own `DbContext`.

`Inbox` and `ParkingLot` entities and their EF configuration live once in `TalentSync.Infrastructure.Messaging`.
The configuration takes the schema name as a parameter; each worker's `DbContext` applies it with its own schema.
Same table shape, separate tables per worker (invariant 4).

## Folder layout

Entities are models used by the rest of the code, so they live in a folder named after the concept
(`Outbox/`, `Inbox/`, `ParkingLot/`, `Enrichment/`). `Persistence/` holds only EF: the `DbContext`, one
`IEntityTypeConfiguration<T>` per entity (Fluent API, never mapping inside `OnModelCreating`) and `Migrations/`.
Configurations are applied explicitly with `ApplyConfiguration(...)`; the shared `Inbox` and `ParkingLot`
configurations take the schema as a constructor parameter.

## Keys (normative)

- Clustered primary key: `Id bigint IDENTITY`. `Guid.CreateVersion7()` is not sequential in SQL Server's
  `uniqueidentifier` sort order, so GUIDs (if any) are non-clustered unique columns.
- `Inbox`: unique index `(Source, EventId)`. Duplicate insert → SqlException 2627 or 2601 → treat as processed.
- `EnrichmentResults`: unique index `(CandidateId, CvHash)`.
- Never check-then-insert to enforce uniqueness (invariant 12). A lookup before the insert is allowed only as a
  skip-optimisation; the unique index is the guard.

## Tables (minimal columns, descriptive once migrations exist)

- `Outbox`: `Id`, `EventId`, `CandidateId`, `Type`, `Envelope` (json), `CreatedAt`, `PublishedAt` (null until published).
  Filtered index on `PublishedAt IS NULL`.
- `Inbox`: `Id`, `Source`, `EventId`, `ProcessedAt`.
- `ParkingLot`: `Id`, `Source` (nullable), `EventId` (nullable), `PartitionId`, `SequenceNumber`, `Envelope`,
  `Error` (type and message, no PII), `ParkedAt`. An envelope that cannot be read has no `Source` or `EventId`:
  it is identified by `PartitionId` + `SequenceNumber`, and `Envelope` holds the raw event body truncated to 4 KB.
  No unique index: a crash between the insert and the checkpoint parks the event twice (accepted in the MVP).
- `EnrichmentResults`: `Id`, `CandidateId`, `CvHash`, `Result` (json), `ModelId`, `CreatedAt`.

## Rules

- Raw SQL for the relay query (`UPDLOCK, READPAST, ROWLOCK`) is fine; keep it in one place.
- Timestamps come from `TimeProvider`, stored as `datetime2` UTC.
- Local SQL Server is `mcr.microsoft.com/mssql/server:2025-latest`; healthcheck uses `sqlcmd -C`.
