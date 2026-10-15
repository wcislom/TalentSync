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
| `Llm:Provider` | Matching.Worker | `Fake` (default in `appsettings.json`) or `Anthropic`; validated on start |
| `Llm:Timeout` | Matching.Worker | per model call, the SDK's retries included (30 s); also the most a slow model delays one event of the partition |
| `Llm:CircuitBreaker:FailureRatio`, `:MinimumThroughput`, `:SamplingDuration`, `:BreakDuration` | Matching.Worker | Polly `CircuitBreakerStrategyOptions`; which exceptions count is decided in code |
| `Llm:Anthropic:ApiKey` | Matching.Worker | user-secrets only, never in the repo; required only when `Provider = Anthropic` |
| `Llm:Anthropic:Model`, `Llm:Anthropic:MaxOutputTokens` | Matching.Worker | `claude-haiku-5-5`, 8000; the ceiling includes adaptive thinking |
| `Llm:Anthropic:MaxRetries` | Matching.Worker | the SDK's retries of 408/409/429/5xx and connection failures; 2 |
| `Llm:Fake:Mode` | Matching.Worker | `Valid`, `InvalidJson`, `OutOfRange`, `Hang`; default when the CV has no `[llm:<Mode>]` marker |
