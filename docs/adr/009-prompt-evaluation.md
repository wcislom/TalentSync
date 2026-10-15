# ADR 009: Offline evaluation of the enrichment prompt

## Status

Accepted

## Context

ADR 007 makes the code around the model safe: masking, a JSON schema response format, domain validation through
`Enrichment.TryCreate`, timeout, circuit breaker, fallback to `NotEnriched`. Unit and integration tests check that
code with `FakeChatClient`, which always gives the same answer. Nothing measures whether the real model, with our
prompt, gives *good* answers: the right skills, no invented ones, a sensible seniority.

The prompt changes over time, and every change gives a new `PromptVersion` (ADR 007). Without a measurement a
prompt change is reviewed by reading the text, and a regression shows up only as worse profiles in the Matching
Platform, which nobody in TalentSync sees.

What makes this unlike the rest of the test suite:

- **The result is a distribution, not pass/fail.** The same CV can give a different answer on every call.
- **Every call is billed** and needs the provider's API key (invariant 17, no secrets in the repo).
- **The output is structured** (a skill list, a seniority, a confidence), so most of its quality can be computed
  with code, without judgement.
- **Correct answers are not unique.** "ASP.NET" and "ASP.NET Core" are both right for the same CV.

## Decision

**An offline evaluation: a golden set of fictional CVs, a console runner, metrics computed in code, a Markdown
report.** It is a measurement tool, not an acceptance scenario, and it never runs in CI.

### What is measured

- **The production path.** The runner calls `IEnrichmentService` (`ChatEnrichmentService`), never a bare
  `IChatClient`: masking, the response schema and `Enrichment.TryCreate` are what we ship, so they are what we
  measure. It builds the service with its dependencies directly; it needs neither SQL nor the Matching Platform,
  and there is no `EnrichmentResults` lookup, so every run is a fresh model call.
- **The unit of measurement is the pair `(PromptVersion, GoldenSetHash)`.** A change to either invalidates a
  report. `ModelId` is recorded in the report but is not part of the pair, as it is not part of the
  `EnrichmentResults` key (ADR 007).
- **`GoldenSetHash`**: SHA-256 over the case files sorted by file name (ordinal), each contributing its file name and
  its content, first 16 hex characters. Content is hashed after removing a UTF-8 BOM and normalising line endings to
  LF, so a Windows checkout and a Linux CI runner compute the same hash.

### Golden set

One JSON file per case in `eval/cases/`. CVs are fictional and anonymised: no real people, no real companies
(Naming in `CLAUDE.md`).

```json
{
  "id": "senior-dotnet",
  "purpose": "One sentence: what this case tests.",
  "cv": "Fictional CV text.",
  "expected": {
    "required":   [["C#"], [".NET", "dotnet", ".NET Core"]],
    "acceptable": ["ASP.NET Core", "Docker"],
    "forbidden":  ["Kubernetes", "Python"],
    "seniority": "Senior",
    "seniorityAlsoAccepted": []
  }
}
```

- `required`: each entry is one skill, given as its accepted spellings. An entry is matched when any spelling is
  among the returned skills.
- `acceptable`: correct if returned, not required.
- `forbidden`: plausible for the CV but not in it. Returning one is a hallucination.
- `seniorityAlsoAccepted`: other levels that are a defensible answer for this CV (e.g. `["Junior"]` for a CV on the
  Junior/Mid boundary). Any level not listed is wrong. A list, not a symmetric tolerance, so a CV can accept
  the neighbour on one side without accepting the one on the other.
- Skill comparison is trimmed and case-insensitive, the same as `Skill` equality. There is no global alias map:
  accepted spellings live in the case that needs them, so a reviewer sees every equivalence the metric relies on.

### Metrics

Computed per case, aggregated as the mean over cases. Undefined for a case → `n/a`, left out of the aggregate.
All but valid rate are over enriched runs only.

| Metric | Definition | Threshold |
|---|---|---|
| Valid rate | `Enriched` / all calls | ≥ 0.95 |
| Recall | matched `required` entries / `required` entries | ≥ 0.80 |
| Precision | returned skills in (`required` ∪ `acceptable`) / returned skills | ≥ 0.70 |
| Hallucination rate | runs with a `forbidden` skill / runs | ≤ 0.10 |
| Seniority accepted | (exact + also accepted) / runs; exact, also accepted and wrong are reported separately | ≥ 0.80 |
| Stability | mean pairwise Jaccard of skill sets across runs | ≥ 0.60 |
| Confidence vs correctness | mean confidence, seniority exact vs not | reported only |

- `Timeout` and `Unavailable` are infrastructure failures, not quality: any of them marks the report **INCOMPLETE**.
  `InvalidOutput` lowers the valid rate.
- 5 runs per case by default. Runs and thresholds come from configuration; the report prints the thresholds used.

### Report and where it runs

- Markdown, `{yyyy-MM-dd}-{PromptVersion}-{sanitised ModelId}.md`. Header with the pair, `ModelId`, provider, runs
  per case, UTC time and the INCOMPLETE flag; then aggregate and per-case tables, and the raw answer of every run,
  from which every metric can be recomputed. Exit code non-zero when INCOMPLETE or below a threshold.
- Run manually, locally, key from user-secrets (Matching.Worker's `UserSecretsId`). Never in CI.
  `--provider Fake` is a zero-cost smoke test of the runner.
- Calls are sequential through one `ChatEnrichmentService`, so the circuit breaker is shared, as in the worker.
- Committed to `eval/reports/` only for prompt versions that reach `main`; exploratory runs go to the gitignored
  `eval/reports/local/`.

### CI guard

A deterministic test fails when `eval/reports/` has no report with the current `PromptVersion` and `GoldenSetHash`
and a `ModelId` other than `FakeChatClient.ModelId`. So a prompt or golden-set change needs a new real report in the
same PR. It does **not** prove a model produced the report (provenance needs a trusted runner, out of scope), does
not require the report to pass its thresholds (the reviewer decides), and ignores model changes.

## Consequences

- A prompt PR carries a measured effect; the diff of two reports is the review.
- Any prompt change needs a paid run, including a cosmetic edit or a package upgrade that changes the schema (ADR 007).
- Editing the golden set invalidates the report too. Expectations are reviewed like code, never weakened to pass.
- A few cases × 5 runs is a smoke signal, not a benchmark: small differences between reports are noise.

## Alternatives considered

- **LLM-as-judge.** Rejected: structured output is measurable with code; a judge also hallucinates and costs.
- **Evaluation in CI on every PR.** Rejected: cost, nondeterministic results, the API key as a CI secret.
- **Exact-match expectations.** Rejected: "ASP.NET" and "ASP.NET Core" are both right.
- **Measuring through a bare `IChatClient`.** Rejected: it would measure a path we do not ship.
