---
paths:
  - "src/**/appsettings*.json"
  - "src/**/DependencyInjection.cs"
  - "src/**/*Options.cs"
  - "deploy/**"
  - "tests/TalentSync.IntegrationTests/Infrastructure/**"
---

# Configuration keys

Proposed names (descriptive: once implemented, the code is the reference).

| Key | Used by | Notes |
|---|---|---|
| `ConnectionStrings:Sql` | all three hosts | each host uses only its own schema |
| `EventHubs:ConnectionString`, `EventHubs:HubName` | all three hosts | hub `ats-events` |
| `EventHubs:TryTimeout`, `EventHubs:MaxRetries` | Ingress.Api | SDK retry of one send; defaults = SDK defaults (60 s, 3) |
| `EventHubs:ConsumerGroup` | workers | `sync-worker` / `matching-worker` |
| `OutboxRelay:BatchSize`, `OutboxRelay:PollInterval`, `OutboxRelay:MaxBackoff` | Ingress.Api | defaults in `appsettings.json` |
| `Checkpoints:BlobConnectionString`, `Checkpoints:Container` | workers | Azurite locally |
| `Consumer:InitialBackoff`, `Consumer:MaxBackoff`, `Consumer:MaxUnclassifiedAttempts` | workers | handler loop; max back-off 30 s, 5 attempts (`resilience.md`) |
| `Teamtailor:BaseUrl`, `Teamtailor:ApiKey`, `Teamtailor:ApiVersion` | workers | API version `20240904` |
| `Teamtailor:SignatureKey` | Ingress.Api | must equal `FakeTeamtailor:SignatureKey` |
| `Teamtailor:QualifiedStageName` | workers | `Qualified` locally. Only Matching.Worker maps stages, but `AddTeamtailorApi` validates it on start in both |
| `Teamtailor:Resilience:*` | workers | `HttpStandardResilienceOptions` of the API client (e.g. `Retry:MaxRetryAttempts` = 2); unset keys keep the library defaults, but the section itself must exist in every `*:Resilience` row or the host fails to build |
| `Crm:BaseUrl` | Sync.Worker | |
| `Crm:Resilience:*` | Sync.Worker | `HttpStandardResilienceOptions` of the CRM client (e.g. `Retry:MaxRetryAttempts` = 2) |
| `MatchingPlatform:BaseUrl` | Matching.Worker | |
| `MatchingPlatform:Resilience:*` | Matching.Worker | `HttpStandardResilienceOptions` of the Matching Platform client (e.g. `Retry:MaxRetryAttempts` = 2) |
| `Llm:Provider` | Matching.Worker | `Gemini` or `Fake` |
| `Llm:Gemini:ApiKey` | Matching.Worker | user-secrets only, never in the repo |
| `Llm:Fake:Mode` | Matching.Worker | `Valid`, `InvalidJson`, `OutOfRange`, `Hang` |
