# TalentSync diagrams

For humans. Descriptive: `docs/architecture.md` is the reference for containers, layout and the key flow.
Keep these diagrams true when a container or the flow changes.

## Containers

```mermaid
flowchart TB
  tt["FakeTeamtailor<br/>webhooks + JSON:API"]
  subgraph ing["Ingress.Api"]
    ep["POST /webhooks/teamtailor<br/>signature v2"]
    outbox[("ingress.Outbox")]
    relay["Outbox relay"]
  end
  eh{{"Event Hubs: ats-events<br/>partition key = candidateId"}}
  subgraph sync["Sync.Worker · group sync-worker"]
    sh["Handler + use case"]
    sdb[("sync.Inbox, sync.ParkingLot")]
  end
  subgraph mat["Matching.Worker · group matching-worker"]
    mh["Handler + use case"]
    mdb[("matching.Inbox, ParkingLot,<br/>EnrichmentResults")]
  end
  crm["FakeCrm"]
  mp["FakeMatchingPlatform"]
  llm["LLM / FakeChatClient"]
  blob[("Azurite: checkpoints")]
  dash["Aspire Dashboard<br/>traces, metrics, logs"]

  tt -->|webhook| ep --> outbox --> relay --> eh
  eh --> sh
  eh --> mh
  sh -->|GET state| tt
  mh -->|GET state| tt
  sh --> sdb
  sh -->|upsert / delete| crm
  mh --> mdb
  mh --> llm
  mh -->|upsert profile| mp
  sh -.-> blob
  mh -.-> blob
  ing -.->|OTLP| dash
  sync -.->|OTLP| dash
  mat -.->|OTLP| dash
```

## Key flow: application qualified

```mermaid
sequenceDiagram
  participant TT as FakeTeamtailor
  participant IN as Ingress.Api
  participant EH as Event Hubs
  participant MW as Matching.Worker
  participant LLM
  participant MP as FakeMatchingPlatform
  TT->>IN: job_application.update (signed)
  IN->>IN: verify, insert outbox
  IN-->>TT: 200
  IN->>EH: relay publishes envelope (ids only)
  EH->>MW: event (partition of candidateId)
  MW->>MW: inbox lookup (skip if seen)
  MW->>TT: GET application + stage (via IRecruitmentSource)
  MW->>TT: GET candidate (CV text)
  MW->>MW: EnrichmentResults lookup by (candidateId, cvHash, promptVersion)
  MW->>LLM: masked CV (only if no stored result)
  MW->>MW: validate, store result
  MW->>MP: PUT /profiles/{candidateId}
  MW->>MW: insert inbox, checkpoint
```

The same flow is one trace in the Aspire Dashboard (ADR 010). Each webhook request is its root; the outbox and
Event Hubs hops are carried by `ingress.Outbox.TraceParent` and the `traceparent` event property. Both consumer
groups appear as branches, Sync.Worker's skipping the event. Span names as the dashboard shows them; the right-hand
column is a comment:

```
POST /webhooks/teamtailor                        ingress-api
└─ webhook job_application.update                ingress-api
   ├─ INSERT                                     ingress.Outbox
   └─ send ats-events                            ingress-api, after the outbox delay
      ├─ process ats-events                      matching-worker, handled
      │  ├─ SELECT [matching].[Inbox]
      │  ├─ GET teamtailor /v1/job-applications
      │  ├─ GET teamtailor /v1/candidates
      │  ├─ SELECT [matching].[EnrichmentResults]
      │  ├─ enrich                               enriched; only if no stored result
      │  │  └─ chat claude-haiku-5-5             model, tokens; "chat" with the Fake provider
      │  ├─ INSERT                               matching.EnrichmentResults
      │  ├─ PUT matching-platform /profiles
      │  ├─ INSERT                               matching.Inbox
      │  └─ checkpoint ats-events
      └─ process ats-events                      sync-worker, skipped_unhandled_type
         └─ checkpoint ats-events
```

An `INSERT` span carries no table name: EF Core prefixes its commands with `SET` statements, and the SqlClient
instrumentation then summarises the command as `INSERT`. The table is in `db.query.text`; values are parameters, never
literals. `checkpoint` is our span around the event processor's checkpoint; the Blob SDK's own requests are not
traced.

## Consumer handler: failure paths

```mermaid
flowchart TB
  ev["event"] --> rd{"read envelope"}
  rd -->|unreadable| pk
  rd -->|unknown or unhandled type| cp
  rd -->|ok| seen{"in inbox?"}
  seen -->|yes| cp["checkpoint"]
  seen -->|no| uc["run use case"]
  uc -->|Done| ib["insert inbox"] --> cp
  uc -->|Permanent / Rejected| pk["insert ParkingLot"] --> cp
  uc -->|transient exception| bo["back off"] --> uc
  uc -->|unclassified exception| cnt{"5th attempt?"}
  cnt -->|no| bo
  cnt -->|yes| pk
  uc -->|shutdown| stop["return, no checkpoint"]
```
