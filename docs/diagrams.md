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
  subgraph sync["Sync.Worker · group crm-sync"]
    sh["Handler + use case"]
    sdb[("sync.Inbox, sync.ParkingLot")]
  end
  subgraph mat["Matching.Worker · group matching"]
    mh["Handler + use case"]
    mdb[("matching.Inbox, ParkingLot,<br/>EnrichmentResults")]
  end
  crm["FakeCrm"]
  mp["FakeMatchingPlatform"]
  llm["Gemini / FakeChatClient"]
  blob[("Azurite: checkpoints")]

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
```

## Key flow: application qualified

```mermaid
sequenceDiagram
  participant TT as FakeTeamtailor
  participant IN as Ingress.Api
  participant EH as Event Hubs
  participant MW as Matching.Worker
  participant LLM as Gemini
  participant MP as FakeMatchingPlatform
  TT->>IN: job_application.update (signed)
  IN->>IN: verify, insert outbox
  IN-->>TT: 200
  IN->>EH: relay publishes envelope (ids only)
  EH->>MW: event (partition of candidateId)
  MW->>MW: inbox lookup (skip if seen)
  MW->>TT: GET application + stage (via IRecruitmentSource)
  MW->>TT: GET candidate (CV text)
  MW->>MW: EnrichmentResults lookup by (candidateId, cvHash)
  MW->>LLM: masked CV (only if no stored result)
  MW->>MW: validate, store result
  MW->>MP: PUT /profiles/{candidateId}
  MW->>MW: insert inbox, checkpoint
```

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
