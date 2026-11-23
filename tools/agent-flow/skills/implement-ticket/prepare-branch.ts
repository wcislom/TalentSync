// npm --prefix tools/agent-flow run prepare-branch <issue-number>
//
// Checks that the implement-ticket skill may start, puts the checkout on the agent's branch and prints the plan.
// In order:
//   1. Refuse unless a trusted author's plan for the current body is approved and no PR exists yet.
//   2. Refuse if the working tree has uncommitted changes: the run must not mix with someone's work.
//   3. Switch to agent/issue-<n>: from the remote branch if a previous run pushed it and crashed before
//      opening the PR, otherwise from main.
//   4. Print the plan. The skill reads it only from here, never from the issue's comments.

import { hashIssueBody } from '../../shared/body-sha.ts';
import { runCommand } from '../../shared/command-runner.ts';
import { config, getAgentBranch } from '../../shared/config.ts';
import { fetchBranch, hasUncommittedChanges, remoteBranchExists, switchToNewBranch } from '../../shared/git-client.ts';
import { findPullRequestFrom, getIssue, getIssueComments } from '../../shared/github-client.ts';
import { parseIssueNumber } from '../../shared/issue-number.ts';
import { logger } from '../../shared/logger.ts';
import { findLatestTrustedPlan } from '../../shared/plan-marker.ts';
import { checkImplementationGate } from './implementation-gate.ts';

const mainBranch = 'main';

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2), 'prepare-branch');
  const agentBranch = getAgentBranch(issueNumber);
  const [issue, comments, pullRequestNumber, uncommittedChanges] = await Promise.all([
    getIssue(issueNumber),
    getIssueComments(issueNumber),
    findPullRequestFrom(agentBranch),
    hasUncommittedChanges(),
  ]);
  // 1. Approved plan for the current body, no PR yet.
  const gate = checkImplementationGate({
    bodySha: hashIssueBody(issue.body),
    plan: findLatestTrustedPlan(comments, config.trustedAuthors),
    planApproved: issue.labels.includes(config.labels.planApproved),
    pullRequestNumber,
  });
  if (!gate.open) throw new Error(`issue #${issueNumber}: ${gate.reason}`);

  // 2. Clean working tree.
  if (uncommittedChanges) throw new Error('the working tree has uncommitted changes: run in a clean checkout');

  // 3. The agent's branch.
  const baseBranch = await remoteBranchExists(agentBranch) ? agentBranch : mainBranch;
  await fetchBranch(baseBranch);
  await switchToNewBranch(agentBranch, baseBranch);
  logger.info(`issue #${issueNumber}: on ${agentBranch} from origin/${baseBranch}`);

  // 4. The plan, as the command's output for the skill, not a log line: it is issue content.
  process.stdout.write(`--- plan ---\n${gate.plan.planText}\n--- end of plan ---\n`);
}

runCommand('prepare-branch', main);
