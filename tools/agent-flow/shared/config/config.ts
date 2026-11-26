import path from 'node:path';

export const config = {
  repoRoot: path.resolve(import.meta.dirname, '..', '..', '..', '..'),
  mainBranch: 'main',
  labels: {
    // The trigger and the trust gate. Only someone with access to the repo can add a label, so the flow never
    // runs on an issue that nobody from the repo chose, whatever event started it.
    agentAssigned: 'agent:assigned',
    agentRunning: 'agent:running',
    planApproved: 'agent:plan-approved',
  },
  lockStaleAfterMinutes: 60,
  // Accounts whose plan comments we trust. Anyone can comment on a public repo, so comments from other accounts
  // are ignored. The bot that runs the flow in GitHub Actions is added here in #47.
  trustedAuthors: ['wcislom'],
  // The flow runs only on issues opened by someone from the repo. The author can edit the body at any time, also
  // during a run, so an issue from anyone else must first be copied into a new issue by someone from the repo.
  trustedAuthorAssociations: ['OWNER', 'MEMBER', 'COLLABORATOR'],
  // Accounts whose agent reviews we trust: you locally, GITHUB_TOKEN in Actions. A separate list, so a workflow
  // token cannot publish a plan that the flow trusts. GitHub returns the Actions login in two forms: with
  // "[bot]" in the REST API (reviews) and without it in GraphQL (review threads).
  trustedReviewAuthors: ['wcislom', 'github-actions[bot]', 'github-actions'],
  // The limit for one run of a flow (issue-to-pr or pr-review). All steps of the run share it.
  budget: {
    maxTurns: 80,
    maxBudgetUsd: 5,
  },
};

export type Budget = typeof config.budget;

// A rerun finds the existing PR by this branch name, so the name must never change for an issue. That is why
// it uses only the issue number. A prefix like feat/ or fix/ taken from a label or the title could change
// between runs and create a second PR. The type (feat, fix, ...) goes into the PR title instead.
export function getAgentBranch(issueNumber: number): string {
  return `agent/issue-${issueNumber}`;
}
