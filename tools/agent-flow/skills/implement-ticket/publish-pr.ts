// npm --prefix tools/agent-flow run publish-pr <issue-number> < pr.md
//
// The last command the implement-ticket skill runs. It commits the agent's change and opens a PR as the agent's
// GitHub App. The skill sends the PR title on the first line of stdin and the PR body after it.
// Steps:
//   1. Stop unless the checkout is on agent/issue-<n>, where prepare-branch put it.
//   2. Check the gate again. While the agent was working, someone may have removed the approval, edited the
//      issue or opened a PR.
//   3. Get the App's token before committing anything. If an environment variable is missing, we stop with
//      nothing changed.
//   4. Commit all changes, with the PR title as the commit message. Stop if there is nothing to commit and no
//      earlier commit on the branch.
//   5. Push without force.
//   6. Open the PR (ready for review, with `Closes #<n>`) and print its URL.
// If the process crashes after step 5 and before step 6, the branch is on GitHub but there is no PR. The next
// run continues from that branch (see prepare-branch) and this command opens the PR.

import { runCommand } from '../../shared/command/command-runner.ts';
import { parseIssueNumber } from '../../shared/command/issue-number.ts';
import { config, getAgentBranch } from '../../shared/config/config.ts';
import {
  commitAll,
  countCommitsAhead,
  getCurrentBranch,
  hasUncommittedChanges,
  pushBranch,
} from '../../shared/git/git-client.ts';
import { getInstallationToken } from '../../shared/github/app-token.ts';
import { getIssue, getIssueComments } from '../../shared/github/issues.ts';
import { createPullRequest, findPullRequestFrom } from '../../shared/github/pull-requests.ts';
import { logger } from '../../shared/logging/logger.ts';
import { hashIssueBody } from '../../shared/plan/body-sha.ts';
import { findLatestTrustedPlan } from '../../shared/plan/plan-marker.ts';
import { checkImplementationGate } from './implementation-gate.ts';
import { parsePullRequestText } from './pull-request-text.ts';

async function readStdin(): Promise<string> {
  process.stdin.setEncoding('utf8');
  let text = '';
  for await (const chunk of process.stdin) text += chunk;
  return text;
}

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2), 'publish-pr');
  const pullRequestText = parsePullRequestText(await readStdin(), issueNumber);
  const agentBranch = getAgentBranch(issueNumber);

  // 1. Are we on the agent's branch?
  const currentBranch = await getCurrentBranch();
  if (currentBranch !== agentBranch) {
    throw new Error(`the checkout is on ${currentBranch || 'a detached HEAD'}, not ${agentBranch}: run prepare-branch`);
  }

  // 2. Check the gate again. A change made after this check and before the PR opens is not caught, but that
  //    window is only a few requests long.
  const [issue, comments, pullRequestNumber] = await Promise.all([
    getIssue(issueNumber),
    getIssueComments(issueNumber),
    findPullRequestFrom(agentBranch),
  ]);
  const gate = checkImplementationGate({
    bodySha: hashIssueBody(issue.body),
    plan: findLatestTrustedPlan(comments, config.trustedAuthors),
    planApproved: issue.labels.includes(config.labels.planApproved),
    pullRequestNumber,
  });
  if (!gate.open) throw new Error(`issue #${issueNumber}: ${gate.reason}`);

  // 3. Get the App's token.
  const token = await getInstallationToken();

  // 4. Commit.
  if (await hasUncommittedChanges()) {
    await commitAll(pullRequestText.title);
  } else if (await countCommitsAhead(config.mainBranch) === 0) {
    throw new Error(`nothing to publish: no changes and no commits on ${agentBranch}`);
  }

  // 5. Push.
  await pushBranch(agentBranch, token);

  // 6. Open the PR. Its URL is safe to log: it holds only the repository name and the PR number.
  const pullRequestUrl = await createPullRequest(
    { head: agentBranch, base: config.mainBranch, ...pullRequestText }, token);
  logger.info(`issue #${issueNumber}: PR opened ${pullRequestUrl}`);
}

runCommand('publish-pr', main);
