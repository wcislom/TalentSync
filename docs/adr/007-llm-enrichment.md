# ADR 007: LLM enrichment

## Status

Accepted

## Context

When a job application reaches the qualified stage, Matching.Worker creates or updates a `ConsultantProfile` (D2).
An LLM suggests skills and seniority from the CV text, with a confidence score. It is a suggestion, never a
decision, and its failure must never block the profile (D5).

The model is unlike every other dependency:

- **Non-deterministic.** The same CV can produce a different answer on every call.
- **Slow, paid or rate-limited.** A call takes seconds; a free tier answers 429 often.
- **Untrusted output.** It can return invalid JSON, values outside the allowed ranges or nothing at all.
- **It receives PII.** A CV contains the candidate's email address and phone number (invariants 14–15).

Event Hubs processes the events of one partition one at a time (ADR 002, ADR 006), so whatever a handler waits for,
every later event of that partition waits too.

The provider is not settled yet. Gemini has a free tier; Claude has none but an official .NET SDK. The decision
below must not depend on that choice.

## Decision

**Two abstraction layers, one of them ours.**

- Port `IEnrichmentService` in `TalentSync.Matching.Application`: CV text in, a suggestion or "failed" out.
  Application never references `Microsoft.Extensions.AI` and knows nothing about prompts, JSON or providers.
- The adapter in `TalentSync.Matching.Infrastructure` builds the prompt and calls `IChatClient`
  (`Microsoft.Extensions.AI`). Behind `IChatClient` the providers are interchangeable.
- The host picks the provider from `Llm:Provider` (a real provider, or `Fake`). Changing the provider is a package
  and a registration, never a change to the use case.

**Flow inside the use case** (details in `.claude/rules/llm.md`):

1. GET the application. Not qualified, or deleted → skip (D2, D4).
2. GET the candidate with the CV. `Deleted` → skip; `Rejected` → rejected (parked); an empty CV →
   `NotEnriched` without calling the model.
3. `CvHash` = SHA-256 of the CV text as fetched (unmasked).
4. Look up `matching.EnrichmentResults` by `(CandidateId, CvHash)`. Found → reuse it, the model is not called.
5. Not found → `IEnrichmentService`. A valid suggestion is stored **before** step 6 (invariant 5, ADR 005).
   A duplicate key on insert (SqlException 2627/2601) means another processor stored one first: read the stored
   row and use it, so the profile always matches what is stored.
6. `PUT /profiles/{candidateId}`: `Enriched` with the suggestion, or `NotEnriched` without one.

**Privacy.** The adapter masks email addresses and phone numbers, so no path reaches the model with unmasked text.
Only the CV text is sent, never the candidate id or other fields. The prompt and the CV are never logged.

**Validation.** Schema first (the request asks for a JSON schema response format, and the reply is parsed against
it), then the domain rules in `TalentSync.Matching.Domain`. Any broken rule invalidates the whole suggestion; it
is never partially accepted. The domain reports a broken rule as a value, not an exception:
`Enrichment.TryCreate` returns `Valid(enrichment)` or `Invalid(reason)`, and the reason is an enum, so model output
cannot reach a log (invariant 14). Invalid output is an expected outcome, and a value is one the compiler makes the
use case handle. The `Enrichment` constructor still throws on a broken rule, but only as a guard against our own bugs.

**Resilience.** A timeout and a circuit breaker (`Polly.Core`, built with our `TimeProvider`, tunables from
configuration), **no retry**. Every failure (timeout, open circuit, 429, invalid output) ends in `NotEnriched`
inside the use case and never reaches the consumer's failure classification. A failure of the profile upsert does,
and its retry finds the stored suggestion in step 4.

**Enrichment runs inline in the handler.** It is synchronous for the event and asynchronous for the webhook:
Ingress answered 200 long before.

**`FakeChatClient`** lives in `src/`, because the running system uses it in demos too (`Llm:Provider = Fake`).
Modes: `Valid`, `InvalidJson`, `OutOfRange`, `Hang`. A marker in the CV text selects the mode per call;
`Llm:Fake:Mode` is the default when there is no marker. The fake records its calls so that tests can attribute them
to a candidate (scenario M5).

## Consequences

- **A slow model delays the whole partition**, by at most the timeout per event. During an outage the circuit
  opens and later events get `NotEnriched` at once instead of each waiting for the timeout.
- **Profiles created while the model is down stay `NotEnriched`** until the next qualification. There is no
  re-enrichment in the MVP.
- **A changed CV is a new model call**, including a change only to an email address or phone number, because the
  hash covers the unmasked text (ADR 005).
- **The mode marker is a test mechanism in product code.** It is read only by `FakeChatClient`, so it has no effect
  with a real provider.
- **On Gemini's free tier, Google may use the data sent to the API.** Acceptable here: the CVs are fictional
  simulator data and are masked before the call. A real deployment would need a paid tier or another provider.
- Dependencies on `Microsoft.Extensions.AI` and one provider package.

## Alternatives considered

- **A separate enrichment stage**: upsert the profile as `NotEnriched` first, publish an internal "enrichment
  requested" event, enrich in a second consumer and upsert again. It would keep the model off the partition's
  critical path. Rejected for the MVP: two upserts per profile, a worker that also produces (a second outbox), more
  emulator configuration, and a new race in which a late enrichment overwrites a newer profile.
- **Calling a provider SDK from the use case, or using `IChatClient` as the port.** Rejected: Application would
  depend on an LLM library, and prompt, masking and parsing would leak into the use case.
- **Retrying the model.** Rejected: cost and rate limits, and a failure ends in `NotEnriched` anyway.
- **Asking the model again on every retry.** Rejected: see ADR 005 and invariant 5.
- **A domain exception for invalid output.** Rejected: invalid output is a normal outcome, not an exceptional one,
  and an exception is not part of the signature. A missed `catch` would let it reach the consumer, which retries an
  unclassified exception 5 times and parks the event instead of creating a `NotEnriched` profile, breaking D5.
  Catching it there would need an exception type shared with `Infrastructure.Messaging`, which the dependency rules
  forbid. A domain exception fits a command that breaks a business rule of an aggregate with state transitions;
  `ConsultantProfile` has none, because it is built whole for each qualification.
- **Masking in the use case.** Rejected: masking in the adapter guarantees that no path reaches the model unmasked.
- **A global switch for the fake's mode, changed by tests at run time.** Rejected: all scenarios share one host,
  so an event of an earlier scenario could be processed in the next scenario's mode.
