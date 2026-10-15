#!/usr/bin/env bash
# Runs the offline evaluation of the enrichment prompt (ADR 009). Edit the variables below, then run:
#   eval/run-eval.sh
# Extra arguments go to the runner and win over the variables, e.g. eval/run-eval.sh --Llm:Timeout 00:01:00
# Exit code 1 = the report is INCOMPLETE or a metric missed its threshold; the report is written either way.
set -euo pipefail

# Fake = zero cost, smoke test of the runner. Anthropic = real model, billed; key from Matching.Worker's
# user-secrets (Llm:Anthropic:ApiKey). The runner has no default provider and stops without one.
PROVIDER="Anthropic"

# Calls per case. Below 2 stability is n/a and the report fails.
RUNS=5

# eval/reports/local = exploratory, gitignored. eval/reports = baseline report to commit.
OUT="eval/reports"

# Empty = Llm:Anthropic:Model from eval/TalentSync.Matching.Evaluation/appsettings.json.
MODEL=""

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

args=(--provider "$PROVIDER" --runs "$RUNS" --out "$OUT")
if [[ -n "$MODEL" ]]; then
  args+=(--Llm:Anthropic:Model "$MODEL")
fi

dotnet run --project eval/TalentSync.Matching.Evaluation -- "${args[@]}" "$@"
