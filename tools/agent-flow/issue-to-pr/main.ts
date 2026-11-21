import { config, getAgentBranch } from '../shared/config.ts';
import type { Budget } from '../shared/config.ts';
import { getIssue, getIssueComments, getIssueTimeline, hasPullRequestFrom } from '../shared/github-client.ts';
import { logger } from '../shared/logger.ts';
import { runSkill } from '../shared/skill-runner.ts';
import type { Session } from '../shared/skill-runner.ts';
import { hashIssueBody } from './body-sha.ts';
import { acquireLock, readLockState, releaseLock } from './lock.ts';
import { decideNextStep } from './next-step.ts';
import { latestTrustedPlan } from './plan-marker.ts';
import { steps } from './steps.ts';

function parseIssueNumber(args: string[]): number {
  if (args.length !== 1 || !/^[1-9][0-9]*$/.test(args[0])) {
    throw new Error('usage: issue-to-pr <issue-number>');
  }
  return Number(args[0]);
}

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2));
  const [issue, issueComments, timelineEvents, pullRequestExists] = await Promise.all([
    getIssue(issueNumber),
    getIssueComments(issueNumber),
    getIssueTimeline(issueNumber),
    hasPullRequestFrom(getAgentBranch(issueNumber)),
  ]);

  const nextStep = decideNextStep({
    lockState: readLockState(issue.labels, timelineEvents, config.labels.agentRunning),
    bodySha: hashIssueBody(issue.body),
    plannedBodySha: latestTrustedPlan(issueComments, config.trustedAuthors)?.plannedBodySha ?? null,
    planApproved: issue.labels.includes(config.labels.planApproved),
    pullRequestExists,
  }, new Date(), config.lockStaleAfterMinutes);

  if (nextStep.kind === 'stop') {
    logger.stopped(issueNumber, nextStep.reason);
    return;
  }

  logger.stepsPlanned(issueNumber, nextStep.steps);
  await acquireLock(issueNumber, config.labels.agentRunning, nextStep.replaceStaleLock);
  let totalTurns = 0;
  let totalCostUsd = 0;
  // Steps of one run share a session, so each sees what the previous one produced.
  let session: Session | undefined;
  try {
    for (const stepName of nextStep.steps) {
      const step = steps[stepName];
      if (step.stub) {
        logger.stepStubbed(stepName, step.skill, issueNumber);
        continue;
      }
      const remainingBudget: Budget = {
        maxTurns: config.budget.maxTurns - totalTurns,
        maxBudgetUsd: config.budget.maxBudgetUsd - totalCostUsd,
      };
      if (remainingBudget.maxTurns <= 0 || remainingBudget.maxBudgetUsd <= 0) {
        throw new Error(`budget spent before step ${stepName}`);
      }
      logger.stepStarted(stepName, step.skill, issueNumber);
      const skillRun = await runSkill(step.skill, String(issueNumber), remainingBudget, session);
      totalTurns += skillRun.turns;
      totalCostUsd += skillRun.stepCostUsd;
      session = skillRun.session;
    }
  } finally {
    logger.runFinished(issueNumber, totalTurns, totalCostUsd);
    try {
      await releaseLock(issueNumber, config.labels.agentRunning);
    } catch (error) {
      // Not rethrown, so it cannot hide the step's own error; the label goes stale after lockStaleAfterMinutes.
      logger.lockReleaseFailed(issueNumber, error);
      process.exitCode = 1;
    }
  }
}

main().catch((error: unknown) => {
  logger.failed(error);
  process.exitCode = 1;
});
