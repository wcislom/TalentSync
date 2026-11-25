import path from 'node:path';

export const config = {
  repoRoot: path.resolve(import.meta.dirname, '..', '..', '..', '..'),
  mainBranch: 'main',
  labels: {
    agentRunning: 'agent:running',
    planApproved: 'agent:plan-approved',
  },
  lockStaleAfterMinutes: 60,
  // Accounts whose plan comments and reviews we trust. Anyone can comment on a public repo, so comments from
  // other accounts are ignored. The bot that runs the flow in GitHub Actions is added here in #47.
  trustedAuthors: ['wcislom'],
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
