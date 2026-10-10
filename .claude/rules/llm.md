---
paths:
  - "src/Matching/**"
  - "tests/TalentSync.Matching.Domain.Tests/**"
---

# LLM enrichment

## Role

The LLM suggests skills and seniority from CV text, each with a confidence score. It never decides a match (D5).
Enrichment is always attempted, but its failure never blocks the profile: the profile is created with status
`NotEnriched`.

## Flow inside the Qualified use case (`TalentSync.Matching.Application`)

1. Compute `CvHash` = SHA-256 of the CV text as fetched (unmasked).
2. Look up `matching.EnrichmentResults` by `(CandidateId, CvHash)`. Found → reuse it, skip the model.
3. Not found → call `IEnrichmentService`. The adapter in `TalentSync.Matching.Infrastructure` masks emails and
   phone numbers, then calls `IChatClient` with a JSON schema response format and a timeout. Masking lives in the
   adapter so that no path reaches the model with unmasked text.
4. Validate: schema first, then domain rules (below). Invalid → treat as failure.
5. On success store the result in `EnrichmentResults` **before** upserting the profile.
6. On failure (timeout, open circuit, 429, invalid output) → profile status `NotEnriched`, nothing stored.
7. Upsert the profile in the Matching Platform.

Step 5 comes before step 7 because the model is non-deterministic (invariant 5): a retry after a crash must replay
the same suggestion, not ask the model again and overwrite the profile with a different answer.

Consequence of step 1: a CV change that touches only an email or phone number gives a new hash and a new model call.
Rare; accepted in the MVP.

LLM failures end in `NotEnriched` here and never reach the consumer's failure classification
(`.claude/rules/resilience.md`). A failure of the Matching Platform upsert (step 7) does: it is retried by the
handler loop, and the retry finds the stored result in step 2 instead of calling the model again.

## Domain validation of a suggestion

- `seniority` ∈ {`Junior`, `Mid`, `Senior`, `Lead`}. Anything else → invalid.
- `confidence` ∈ [0, 1].
- Skills: non-empty names, trimmed, max 30, de-duplicated case-insensitively.
- These rules live in `TalentSync.Matching.Domain`, not in the LLM adapter.

## Providers

- Port: `IEnrichmentService` in `TalentSync.Matching.Application`. The adapter in
  `TalentSync.Matching.Infrastructure` masks the CV and uses `IChatClient` from `Microsoft.Extensions.AI`.
  Application never references `Microsoft.Extensions.AI`.
- Default: Gemini, key from user-secrets `Llm:Gemini:ApiKey`. **UNCERTAIN:** whether structured output works through
  Gemini's OpenAI-compatible endpoint. Verify on the first real call and report before building on it.
- `FakeChatClient` (selected by `Llm:Provider = Fake`, mode from `Llm:Fake:Mode`): modes `Valid`, `InvalidJson`,
  `OutOfRange`, `Hang`. `Valid` always returns the same output.
- `FakeChatClient` **counts its calls** in a way tests can attribute to a candidate (scenario M5 asserts the model
  was called exactly once). How tests read the count is part of the test hosting decision (testing rule).
- Used by scenarios M1–M5 and by the "LLM timeout" demo.
- The free tier has low rate limits: 429 from the model is a normal failure and ends in `NotEnriched`.
- Never log the prompt or the CV text.
