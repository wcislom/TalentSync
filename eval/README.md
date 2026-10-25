# Enrichment prompt evaluation

The runner sends each fictional CV from `cases/` to the model several times (5 by default) and compares every answer
with what the case expects. The decision and the formulas are in [ADR 009](../docs/adr/009-prompt-evaluation.md).

## What a case expects

Example: `cases/senior-dotnet.json`, a CV of a senior C# developer.

- `required`: skills the model must find, e.g. C#, .NET, SQL Server.
- `acceptable`: also correct if returned, but not needed, e.g. Docker, Git.
- `forbidden`: skills a model could easily guess for such a CV, but the CV does not mention, e.g. Kubernetes, Python.
- `seniority`: the expected level (`Senior`), plus `seniorityAlsoAccepted` for borderline CVs.

## The metrics

| Metric | Question | Example | Target |
|---|---|---|---|
| Valid rate | How many answers could we use at all? | 5 calls, 1 broken JSON → 0.80 | ≥ 0.95 |
| Recall | Of the `required` skills, how many did it find? | Found 4 of 5 → 0.80 | ≥ 0.80 |
| Precision | Of the skills it returned, how many are listed as correct? | 10 returned, 9 in `required` or `acceptable` → 0.90 | ≥ 0.70 |
| Hallucination rate | In how many answers did it invent a `forbidden` skill? | 1 of 5 answers has Kubernetes → 0.20 | ≤ 0.10 |
| Seniority accepted | In how many answers is the level right? | 4 of 5 say `Senior` → 0.80 | ≥ 0.80 |
| Stability | Does it give the same skills every time? | Identical lists in all 5 answers → 1.00 | ≥ 0.60 |
| Confidence | Is it more sure of itself when it is right? | 0.90 when the level is right, 0.50 when wrong: good | reported only |

Each metric is computed per case, then averaged over cases. Targets are in `Evaluation:Thresholds`.

## Common misreadings

- **Low precision is not always the model's fault.** A skill that is in the CV but missing from `acceptable`
  lowers precision too. Check the raw answers; often the case needs a longer list.
  Hallucination rate counts only `forbidden` skills, so a hallucination is always a real error.
- **High stability is not quality.** A model that returns the same wrong list every time is fully stable.
- **INCOMPLETE is not a bad score.** Timeouts and an unavailable provider do not lower any metric, they make the whole
  report INCOMPLETE. Rerun it. Only broken answers lower the valid rate.
- **`n/a`** means the metric cannot be computed for that case (e.g. stability needs at least 2 valid answers).
  It is left out of the average. With `--runs 1` stability is `n/a` everywhere, and the report fails.
- **Small differences are noise.** 6 cases × 5 runs is a smoke signal, not a benchmark. Compare reports only when
  their `GoldenSetHash` is the same.
