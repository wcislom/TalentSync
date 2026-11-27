# ADR 013: Agent flow from issue to PR

## Status

Accepted

## Context

A coding agent can turn an issue into a PR. The repo is public: anyone can open an issue, comment or open a PR,
so their text is untrusted input to the agent (prompt injection). The agent must not be able to put code on `main`
by itself.

## Decision

- **Two human gates: the plan and the merge.** The agent writes a plan as a comment. It writes code only after
  someone adds `agent:plan-approved`. The PR needs an approval before merge.
- **The gates are code and repository settings, not prompts.** The orchestrator (`tools/agent-flow`) decides
  the next step from GitHub state in a tested pure function. Rulesets stop the agent from merging or pushing
  outside `agent/**`.
- **An approval is for one version of the issue.** The plan stores a hash of the issue body. If the body changes,
  the plan is out of date and the agent writes a new one, which removes the approval.
- **The flow runs only on trusted issues.** The issue needs the label `agent:assigned` (only people with access
  can add labels) and an author who is an owner, member or collaborator.
- **The agent has its own identity: a GitHub App.** Plans, commits and PRs come from `talentsync-agent[bot]`.
  The user cannot approve their own PR, so the agent cannot use the user's account. The App's token lasts one
  hour and cannot change workflows.
- **Each step gets only the tools it needs.** Skills in `.claude/skills/` list their allowed commands. Read-only
  skills (explain, review) cannot write. The review skill gets no token: the script checks its output and
  publishes it.
- **Workflows and tools always run from `main`**, so a branch cannot change the tools of its own run.
- **A budget per run** (turns and dollars). A run that reaches it stops with a warning and can be started again.
- **Own orchestrator on the Claude Agent SDK**, not `anthropics/claude-code-action`. The action runs one prompt
  per event; the gates above would live in YAML and in the prompt, not in tested code.

## Consequences

- Every step can run again safely: state lives in GitHub (body, plan comment, labels, branch, PR), not in the
  agent. The plan comment is edited in place, the push is never forced, an existing PR ends the flow.
- One issue at a time. The user picks the next issue.
- **Cost:** our own code to maintain (`tools/agent-flow`, with its own tests in CI).
- **Cost:** nothing reacts to review comments yet. Fixes after a review are done by hand.
- **Cost:** the agent review is not a required check, so a Claude outage does not block merges. A PR can be
  merged without it.
- The user's own PRs merge by admin bypass, because they cannot approve them.

## Alternatives considered

- **`anthropics/claude-code-action`.** Less code, but the flow's gates would be in YAML and prompts, not in tested
  code.
- **A personal access token instead of a GitHub App.** Rejected: the agent's PRs would be the user's own PRs, and
  the user could not approve them. A PAT also lives for months, not one hour.
- **No plan gate (issue → PR at once).** Rejected: a wrong plan is cheaper to fix than a wrong PR.
