// npm --prefix tools/agent-flow run publish-pr <issue-number> < pr.md
//
// Commits the implement-ticket skill's change and opens its PR as the agent's GitHub App. The title is the first
// line on stdin, the body the rest. In order:
//   1. Refuse unless the checkout is on agent/issue-<n> (prepare-branch put it there).
//   2. Check the gate again: the approval may have been removed, the body edited or a PR opened while the skill
//      worked.
//   3. Get the App's installation token, before anything is committed: a missing variable changes nothing.
//   4. Commit everything with the PR title as the message. Nothing to commit and no earlier commit = refuse.
//   5. Push without force.
//   6. Open the PR, ready for review, with `Closes #<n>`, and print its URL.
// A crash between 5 and 6 leaves the branch pushed and no PR: the next run starts from the remote branch
// (prepare-branch), and this command opens the PR.

import { hashIssueBody } from '../../shared/body-sha.ts';
import { runCommand } from '../../shared/command-runner.ts';
import { config, getAgentBranch } from '../../shared/config.ts';
import {
  commitAll,
  countCommitsAhead,
  getCurrentBranch,
  hasUncommittedChanges,
  pushBranch,
} from '../../shared/git-client.ts';
import { getInstallationToken } from '../../shared/github-app-token.ts';
import { createPullRequest, findPullRequestFrom, getIssue, getIssueComments } from '../../shared/github-client.ts';
import { parseIssueNumber } from '../../shared/issue-number.ts';
import { logger } from '../../shared/logger.ts';
import { findLatestTrustedPlan } from '../../shared/plan-marker.ts';
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

  // 1. The agent's branch.
  const currentBranch = await getCurrentBranch();
  if (currentBranch !== agentBranch) {
    throw new Error(`the checkout is on ${currentBranch || 'a detached HEAD'}, not ${agentBranch}: run prepare-branch`);
  }

  // 2. The gate, again. A change after this check and before the PR opens is not caught: a few requests long.
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

  // 3. Token.
  const token = await getInstallationToken();

  // 4. Commit.
  if (await hasUncommittedChanges()) {
    await commitAll(pullRequestText.title);
  } else if (await countCommitsAhead(config.mainBranch) === 0) {
    throw new Error(`nothing to publish: no changes and no commits on ${agentBranch}`);
  }

  // 5. Push.
  await pushBranch(agentBranch, token);

  // 6. PR. Its URL is the skill's output; it holds only the repository and the PR number.
  const pullRequestUrl = await createPullRequest(
    { head: agentBranch, base: config.mainBranch, ...pullRequestText }, token);
  logger.info(`issue #${issueNumber}: PR opened ${pullRequestUrl}`);
}

runCommand('publish-pr', main);
