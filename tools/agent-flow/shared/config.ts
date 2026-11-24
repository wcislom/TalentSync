import path from 'node:path';

export const config = {
  repoRoot: path.resolve(import.meta.dirname, '..', '..', '..'),
  mainBranch: 'main',
  labels: {
    agentRunning: 'agent:running',
    planApproved: 'plan-approved',
  },
  lockStaleAfterMinutes: 60,
  // Only their plan comments count. The workflow bot (#47) joins this list once it exists.
  trustedAuthors: ['wcislom'],
  // Per orchestrator run, shared by all its steps.
  budget: {
    maxTurns: 80,
    maxBudgetUsd: 5,
  },
};

export type Budget = typeof config.budget;

// The branch name is how a rerun finds the existing PR, so it is derived from the issue number alone.
// A type (feat/fix) from a label or the title could change between runs and lead to a second PR;
// the type goes in the PR title, which becomes the squash commit message.
export function getAgentBranch(issueNumber: number): string {
  return `agent/issue-${issueNumber}`;
}
