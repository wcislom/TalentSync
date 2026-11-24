// npm --prefix tools/agent-flow run prepare-branch <issue-number>
//
// The first command the implement-ticket skill runs. It checks that the agent may start, moves the checkout to
// the agent's branch and prints the approved plan for the skill.
// Steps:
//   1. Stop unless there is an approved plan from a trusted author, written for the current issue body, and no
//      PR exists yet.
//   2. Stop if the working tree has uncommitted changes. The agent must not mix its work with someone else's.
//   3. Switch to the branch agent/issue-<n>. If an earlier run pushed this branch and then crashed before it
//      opened the PR, start from that branch. Otherwise start from main.
//   4. Print the plan. The skill takes the plan only from here, never from the issue comments, because anyone
//      can post a comment.

import { runCommand } from '../../shared/command/command-runner.ts';
import { parseIssueNumber } from '../../shared/command/issue-number.ts';
import { config, getAgentBranch } from '../../shared/config/config.ts';
import {
  fetchBranch,
  hasUncommittedChanges,
  remoteBranchExists,
  switchToNewBranch,
} from '../../shared/git/git-client.ts';
import { getIssue, getIssueComments } from '../../shared/github/issues.ts';
import { findPullRequestFrom } from '../../shared/github/pull-requests.ts';
import { logger } from '../../shared/logging/logger.ts';
import { hashIssueBody } from '../../shared/plan/body-sha.ts';
import { findLatestTrustedPlan } from '../../shared/plan/plan-marker.ts';
import { checkImplementationGate } from './implementation-gate.ts';

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2), 'prepare-branch');
  const agentBranch = getAgentBranch(issueNumber);
  const [issue, comments, pullRequestNumber, uncommittedChanges] = await Promise.all([
    getIssue(issueNumber),
    getIssueComments(issueNumber),
    findPullRequestFrom(agentBranch),
    hasUncommittedChanges(),
  ]);
  // 1. Is there an approved plan for the current body, and no PR yet?
  const gate = checkImplementationGate({
    bodySha: hashIssueBody(issue.body),
    plan: findLatestTrustedPlan(comments, config.trustedAuthors),
    planApproved: issue.labels.includes(config.labels.planApproved),
    pullRequestNumber,
  });
  if (!gate.open) throw new Error(`issue #${issueNumber}: ${gate.reason}`);

  // 2. Is the working tree clean?
  if (uncommittedChanges) throw new Error('the working tree has uncommitted changes: run in a clean checkout');

  // 3. Switch to the agent's branch.
  const baseBranch = await remoteBranchExists(agentBranch) ? agentBranch : config.mainBranch;
  await fetchBranch(baseBranch);
  await switchToNewBranch(agentBranch, baseBranch);
  logger.info(`issue #${issueNumber}: on ${agentBranch} from origin/${baseBranch}`);

  // 4. Print the plan. It goes to stdout for the skill, not through the logger: it is issue content and must
  //    not end up in public logs.
  process.stdout.write(`--- plan ---\n${gate.plan.planText}\n--- end of plan ---\n`);
}

runCommand('prepare-branch', main);
