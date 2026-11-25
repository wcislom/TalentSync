// npm --prefix tools/agent-flow run pr-review <pr-number>
//
// Runs the review-pr skill on one PR and publishes the result as one review (event COMMENT).
// Anyone can open a PR, so its content is not trusted. That is why the skill can only read.
// This script checks the skill's output and publishes it. The skill never gets a token.
// The review is published with the current gh login: your account locally, GITHUB_TOKEN in Actions.
// GITHUB_TOKEN cannot approve a PR unless the repository settings allow it.
//
// Steps:
//   1. Skip the PR if it is closed, a draft or from a fork.
//   2. Skip it if this commit already has our review.
//   3. Run /review-pr <n>.
//   4. Read the PR again. If someone pushed during the review, publish nothing:
//      the new commit gets its own run.
//   5. Sort the findings. A finding on a diff line becomes an inline comment. Other findings go into the
//      review body. A finding that already has an open thread (same file, line and rule) is skipped.
//   6. Publish the review in one API call.

import { runCommand } from '../../shared/command/command-runner.ts';
import { parseIssueNumber } from '../../shared/command/issue-number.ts';
import { config } from '../../shared/config/config.ts';
import { getPullRequest, getPullRequestDiff } from '../../shared/github/pull-requests.ts';
import type { PullRequest } from '../../shared/github/pull-requests.ts';
import { createReview, getPullRequestReviews, getUnresolvedReviewThreads } from '../../shared/github/reviews.ts';
import { logger } from '../../shared/logging/logger.ts';
import { formatSkillRun, runSkill } from '../../shared/runner/skill-runner.ts';
import { parseCommentableLines } from './diff-parser.ts';
import { buildReview } from './review-builder.ts';
import { isAlreadyReviewed } from './review-markers.ts';
import { parseSkillOutput } from './skill-output-parser.ts';

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

  // 1. Can we review it?
  const skipReason = findSkipReason(pullRequest);
  if (skipReason !== null) {
    logger.info(`PR #${pullRequestNumber}: skipped, ${skipReason}`);
    return;
  }

  // 2. Already reviewed?
  if (isAlreadyReviewed(reviews, pullRequest.headSha, config.trustedReviewAuthors)) {
    logger.info(`PR #${pullRequestNumber}: skipped, already reviewed at ${pullRequest.headSha.slice(0, 7)}`);
    return;
  }

  // 3. Run the skill.
  logger.startGroup(`/${skill} ${pullRequestNumber}`);
  const skillRun = await runSkill(skill, String(pullRequestNumber), config.budget);
  logger.info(`/${skill} ${pullRequestNumber}: ${formatSkillRun(skillRun)}`);
  logger.endGroup();
  if (!skillRun.succeeded) throw new Error(`/${skill} stopped: ${skillRun.outcome}`);
  const skillOutput = parseSkillOutput(skillRun.resultText);

  // 4. Did the head change?
  const [currentPullRequest, diff, openThreads] = await Promise.all([
    getPullRequest(pullRequestNumber),
    getPullRequestDiff(pullRequestNumber),
    getUnresolvedReviewThreads(pullRequestNumber),
  ]);
  if (currentPullRequest.headSha !== pullRequest.headSha) {
    logger.info(`PR #${pullRequestNumber}: not published, the head moved during the review`);
    return;
  }

  // 5. Sort the findings.
  const review = buildReview({
    headSha: pullRequest.headSha,
    skillOutput,
    commentableLines: parseCommentableLines(diff),
    openThreads,
    trustedAuthors: config.trustedReviewAuthors,
  });

  // 6. Publish.
  await createReview(pullRequestNumber, review);
  logger.info(`PR #${pullRequestNumber}: review published at ${pullRequest.headSha.slice(0, 7)}, `
    + `${review.comments.length} inline, ${review.findingsInBody} in the body, `
    + `${review.findingsRepeated} already open`);
}

runCommand('pr-review', main);
