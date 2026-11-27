# Agent flow

Scripts that run the Claude Code skills from `.claude/skills/` through the Claude Agent SDK. Why it is built this
way: [ADR 013](../../docs/adr/013-agent-flow.md). Not part of the product; references nothing in `src/`.

Two flows:

- **issue-to-pr**: an issue → a plan → a PR.
- **pr-review**: a review of a PR, as comments.

## issue-to-pr

```
label agent:assigned → explain → plan (comment) → [human: agent:plan-approved] → implement → PR
```

Every run reads the issue from GitHub and decides the next step (`flows/issue-to-pr/next-step.ts`):

| State of the issue | What the run does |
|---|---|
| No `agent:assigned` | Stops |
| Author is not owner, member or collaborator | Stops |
| `agent:running` younger than 60 min | Stops: another run is working |
| A PR from `agent/issue-<n>` exists | Stops: done |
| No plan, or the plan is for an older body | Explain + plan. The new plan removes `agent:plan-approved` |
| Plan is current, not approved | Stops: waits for a human |
| Plan is current and approved | Implement: tests first, `dotnet build` and `dotnet test` green, then the PR |

- **Labels.** `agent:assigned` starts the flow, `agent:plan-approved` approves the plan, `agent:running` is the
  lock: the flow adds and removes it, never add it by hand.
- **The lock is not perfect.** Two runs that start at the same moment can both continue. That is safe: every step
  can run twice.
- **Trusted plans only.** A plan counts only if the agent's bot or the owner wrote it. Comments from others are
  ignored.
- **One session per run.** The plan step sees what the explain step found.
- **Budget per run:** 80 turns, $5 (`shared/config/config.ts`). Reaching it is a warning, not a failure.

## pr-review

Runs `/review-pr` on a PR that is open, ready and not from a fork. It checks the diff against the repo's rules
(domain rules, invariants, ADRs, scenarios), not general style.

- The skill only reads and gets no token. The script checks its output and publishes one review (`COMMENT`).
- Findings on diff lines become inline comments. The rest go into the review body.
- A commit already reviewed is skipped. If someone pushes during the review, nothing is published.

## Where it runs

- **GitHub Actions.** `.github/workflows/agent-flow.yml` runs issue-to-pr when one of the two labels is added, or
  by hand ("Run workflow", with an issue number). The "Agent review" job in `ci.yml` runs pr-review on every PR,
  after Build.
- **Locally**, with your Claude Code login:

  ```bash
  npm --prefix tools/agent-flow ci
  npm --prefix tools/agent-flow run issue-to-pr <issue-number>
  npm --prefix tools/agent-flow run pr-review <pr-number>
  npm --prefix tools/agent-flow test
  ```

  Node version: `.nvmrc`. Locally, pushes and PRs also go through the App. Set `AGENT_APP_ID`,
  `AGENT_APP_INSTALLATION_ID` and `AGENT_APP_PRIVATE_KEY_PATH`.

## Setup

- **GitHub App `talentsync-agent`**, installed on the repo: contents, pull requests and issues write. Nothing else.
- **Secrets:** `CLAUDE_CODE_OAUTH_TOKEN` (from `claude setup-token`), `AGENT_APP_PRIVATE_KEY`.
  **Variable:** `AGENT_APP_CLIENT_ID`.
- **Rulesets:**
  - *Protect main*: a PR with one approval after the last push. Required checks: Build, Test, Admin app,
    Agent flow tests. A renamed job blocks every merge.
  - *Agent branches only*: everyone except admins, the App too, can create, push or delete only `agent/**`
    branches.

## Example

Issue #68 → plan from the bot → `agent:plan-approved` → PR #69 from `talentsync-agent[bot]`, with CI and the
agent review. Cost: $0.23 for explain and plan, $0.10 for implement.
