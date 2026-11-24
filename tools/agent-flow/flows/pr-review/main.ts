// npm --prefix tools/agent-flow run pr-review <pr-number>
//
// Runs the review-pr skill on a PR and publishes its findings as one review with event COMMENT. The skill only
// reads: the PR is untrusted input, so the skill gets no tool that writes, and this flow publishes its output
// after checking it against the skill's contract. Publishes with the gh login (locally the user, in Actions
// GITHUB_TOKEN, which cannot approve while the repository does not allow it). In order:
//   1. Stop unless the PR is open, ready for review and from this repository (a fork's PR is not reviewed).
//   2. Stop if a trusted author already published the agent's review for this head commit.
//   3. Run /review-pr <n>.
//   4. Read the PR again: if the head moved during the review, publish nothing (the new head gets its own run).
//   5. Anchored findings on a diff line become inline comments; others go into the review body; a finding with
//      an unresolved thread from an earlier run on the same path, line and rule is not repeated.
//   6. Publish the review in one call.

import { runCommand } from '../../shared/command-runner.ts';
import { config } from '../../shared/config.ts';
import {
  createReview,
  getPullRequest,
  getPullRequestDiff,
  getPullRequestReviews,
  getUnresolvedReviewThreads,
} from '../../shared/github-client.ts';
import type { PullRequest } from '../../shared/github-client.ts';
import { parseIssueNumber } from '../../shared/issue-number.ts';
import { logger } from '../../shared/logger.ts';
import { formatSkillRun, runSkill } from '../../shared/skill-runner.ts';
import { parseCommentableLines } from './diff-lines.ts';
import { buildReviewDraft, isAlreadyReviewed } from './review-draft.ts';
import { parseReviewOutput } from './review-output.ts';

const skill = 'review-pr';

function findSkipReason(pullRequest: PullRequest): string | null {
  if (pullRequest.state !== 'OPEN') return `the PR is ${pullRequest.state.toLowerCase()}`;
  if (pullRequest.isDraft) return 'the PR is a draft';
  if (pullRequest.isCrossRepository) return 'the PR comes from a fork';
  return null;
}

async function main(): Promise<void> {
  const pullRequestNumber = parseIssueNumber(process.argv.slice(2), 'pr-review', 'pr-number');
  const [pullRequest, reviews] = await Promise.all([
    getPullRequest(pullRequestNumber),
    getPullRequestReviews(pullRequestNumber),
  ]);

  // 1. Reviewable.
  const skipReason = findSkipReason(pullRequest);
  if (skipReason !== null) {
    logger.info(`PR #${pullRequestNumber}: skipped, ${skipReason}`);
    return;
  }

  // 2. Not reviewed yet at this head.
  if (isAlreadyReviewed(reviews, pullRequest.headSha, config.trustedAuthors)) {
    logger.info(`PR #${pullRequestNumber}: skipped, already reviewed at ${pullRequest.headSha.slice(0, 7)}`);
    return;
  }

  // 3. The skill.
  logger.startGroup(`/${skill} ${pullRequestNumber}`);
  const skillRun = await runSkill(skill, String(pullRequestNumber), config.budget);
  logger.info(`/${skill} ${pullRequestNumber}: ${formatSkillRun(skillRun)}`);
  logger.endGroup();
  if (!skillRun.succeeded) throw new Error(`/${skill} stopped: ${skillRun.outcome}`);
  const reviewOutput = parseReviewOutput(skillRun.resultText);

  // 4. Same head.
  const [currentPullRequest, diff, openThreads] = await Promise.all([
    getPullRequest(pullRequestNumber),
    getPullRequestDiff(pullRequestNumber),
    getUnresolvedReviewThreads(pullRequestNumber),
  ]);
  if (currentPullRequest.headSha !== pullRequest.headSha) {
    logger.info(`PR #${pullRequestNumber}: not published, the head moved during the review`);
    return;
  }

  // 5. Inline, in the body, or already open.
  const reviewDraft = buildReviewDraft({
    headSha: pullRequest.headSha,
    reviewOutput,
    commentableLines: parseCommentableLines(diff),
    openThreads,
    trustedAuthors: config.trustedAuthors,
  });

  // 6. One call.
  await createReview(pullRequestNumber, reviewDraft);
  logger.info(`PR #${pullRequestNumber}: review published at ${pullRequest.headSha.slice(0, 7)}, `
    + `${reviewDraft.comments.length} inline, ${reviewDraft.findingsInBody} in the body, `
    + `${reviewDraft.findingsRepeated} already open`);
}

runCommand('pr-review', main);
