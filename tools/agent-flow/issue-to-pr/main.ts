import { config, getAgentBranch } from '../shared/config.ts';
import { findPullRequestFrom, getIssue, getIssueComments, getIssueTimeline } from '../shared/github-client.ts';
import { RunBudget } from '../shared/run-budget.ts';
import { runSkill } from '../shared/skill-runner.ts';
import type { Session } from '../shared/skill-runner.ts';
import { hashIssueBody } from './body-sha.ts';
import { flowLog } from './flow-log.ts';
import { acquireLock, readLockState, releaseLock } from './lock.ts';
import { decideNextStep, NextStepKind } from './next-step.ts';
import { latestTrustedPlan } from './plan-marker.ts';
import { steps } from './steps.ts';

function parseIssueNumber(args: string[]): number {
  if (args.length !== 1 || !/^[1-9][0-9]*$/.test(args[0])) {
    throw new Error('usage: issue-to-pr <issue-number>');
  }
  return Number(args[0]);
}

// Sets the exit code instead of calling process.exit(), so the lock release and pending log output still finish.
function failProcess(): void {
  process.exitCode = 1;
}

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2));
  const [issue, issueComments, timelineEvents, pullRequestNumber] = await Promise.all([
    getIssue(issueNumber),
    getIssueComments(issueNumber),
    getIssueTimeline(issueNumber),
    findPullRequestFrom(getAgentBranch(issueNumber)),
  ]);

  const nextStep = decideNextStep({
    lockState: readLockState(issue.labels, timelineEvents, config.labels.agentRunning),
    bodySha: hashIssueBody(issue.body),
    plannedBodySha: latestTrustedPlan(issueComments, config.trustedAuthors)?.plannedBodySha ?? null,
    planApproved: issue.labels.includes(config.labels.planApproved),
    pullRequestNumber,
  }, new Date(), config.lockStaleAfterMinutes);

  flowLog.nextStep(issueNumber, nextStep);
  if (nextStep.kind === NextStepKind.Stop) return;

  await acquireLock(issueNumber, config.labels.agentRunning, nextStep.replaceStaleLock);
  flowLog.lockTaken(issueNumber, nextStep.replaceStaleLock);
  const runBudget = new RunBudget(config.budget);
  // Steps of one run share a session, so each sees what the previous one produced.
  let session: Session | undefined;
  try {
    for (const stepName of nextStep.steps) {
      const step = steps[stepName];
      if (step.stub) {
        flowLog.stepStubbed(stepName, step.skill, issueNumber);
        continue;
      }
      if (runBudget.isSpent()) throw new Error(`budget spent before step ${stepName}`);
      flowLog.stepStarted(stepName, step.skill, issueNumber);
      const skillRun = await runSkill(step.skill, String(issueNumber), runBudget.remaining(), session);
      runBudget.record(skillRun);
      session = skillRun.session;
      flowLog.stepFinished(stepName, step.skill, issueNumber, skillRun, runBudget.remaining());
      if (!skillRun.succeeded) throw new Error(`step ${stepName} stopped: ${skillRun.outcome}`);
    }
  } finally {
    try {
      await releaseLock(issueNumber, config.labels.agentRunning);
      flowLog.lockReleased(issueNumber);
    } catch (error) {
      // Not rethrown, so it cannot hide the step's own error.
      flowLog.lockReleaseFailed(issueNumber, error);
      failProcess();
    }
    flowLog.runFinished(issueNumber, runBudget);
  }
}

main().catch((error: unknown) => {
  flowLog.failed(error);
  failProcess();
});
