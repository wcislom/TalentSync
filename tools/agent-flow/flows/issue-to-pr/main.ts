import { failProcess } from '../../shared/command/command-runner.ts';
import { parseIssueNumber } from '../../shared/command/issue-number.ts';
import { config, getAgentBranch } from '../../shared/config/config.ts';
import { getIssue, getIssueComments, getIssueTimeline } from '../../shared/github/issues.ts';
import { findPullRequestFrom } from '../../shared/github/pull-requests.ts';
import { hashIssueBody } from '../../shared/plan/body-sha.ts';
import { findLatestTrustedPlan } from '../../shared/plan/plan-marker.ts';
import { BudgetSpentError, RunBudget } from '../../shared/runner/run-budget.ts';
import { runSkill } from '../../shared/runner/skill-runner.ts';
import type { Session } from '../../shared/runner/skill-runner.ts';
import { flowLog } from './flow-log.ts';
import { acquireLock, readLockState, releaseLock } from './lock.ts';
import { decideNextStep, NextStepKind } from './next-step.ts';
import type { FlowState } from './next-step.ts';
import { steps } from './steps.ts';
import type { StepName } from './steps.ts';

async function readFlowState(issueNumber: number): Promise<FlowState> {
  const [issue, issueComments, timelineEvents, pullRequestNumber] = await Promise.all([
    getIssue(issueNumber),
    getIssueComments(issueNumber),
    getIssueTimeline(issueNumber),
    findPullRequestFrom(getAgentBranch(issueNumber)),
  ]);
  return {
    assigned: issue.labels.includes(config.labels.agentAssigned),
    authorTrusted: config.trustedAuthorAssociations.includes(issue.authorAssociation),
    lockState: readLockState(issue.labels, timelineEvents, config.labels.agentRunning),
    bodySha: hashIssueBody(issue.body),
    plannedBodySha: findLatestTrustedPlan(issueComments, config.trustedAuthors)?.plannedBodySha ?? null,
    planApproved: issue.labels.includes(config.labels.planApproved),
    pullRequestNumber,
  };
}

async function runSteps(issueNumber: number, stepNames: StepName[], runBudget: RunBudget): Promise<void> {
  // All steps of one run use the same Claude session. So the Plan step can see what the Explain step found,
  // without passing it through GitHub or the logs.
  let session: Session | undefined;
  for (const stepName of stepNames) {
    const step = steps[stepName];
    if (step.stub) {
      flowLog.logStepStubbed(stepName, step.skill, issueNumber);
      continue;
    }
    if (runBudget.isSpent()) throw new BudgetSpentError(`budget spent before step ${stepName}`);
    flowLog.logStepStarted(stepName, step.skill, issueNumber);
    const skillRun = await runSkill(step.skill, String(issueNumber), runBudget.getRemaining(), session);
    runBudget.record(skillRun);
    session = skillRun.session;
    flowLog.logStepFinished(stepName, step.skill, issueNumber, skillRun, runBudget.getRemaining());
    if (skillRun.stoppedByBudget) throw new BudgetSpentError(`step ${stepName} stopped: ${skillRun.outcome}`);
    if (!skillRun.succeeded) throw new Error(`step ${stepName} stopped: ${skillRun.outcome}`);
  }
}

// It never throws. If a step failed and then releasing the lock failed too, an exception here would replace
// the step's error, and we would lose the real reason.
async function tryReleaseLock(issueNumber: number): Promise<void> {
  try {
    await releaseLock(issueNumber, config.labels.agentRunning);
    flowLog.logLockReleased(issueNumber);
  } catch (error) {
    flowLog.logLockReleaseFailed(issueNumber, error);
    failProcess();
  }
}

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2), 'issue-to-pr');
  const flowState = await readFlowState(issueNumber);
  const nextStep = decideNextStep(flowState, new Date(), config.lockStaleAfterMinutes);
  flowLog.logNextStep(issueNumber, nextStep);
  if (nextStep.kind === NextStepKind.Stop) return;

  await acquireLock(issueNumber, config.labels.agentRunning, nextStep.replaceStaleLock);
  flowLog.logLockTaken(issueNumber, nextStep.replaceStaleLock);
  const runBudget = new RunBudget(config.budget);
  try {
    await runSteps(issueNumber, nextStep.steps, runBudget);
  } finally {
    await tryReleaseLock(issueNumber);
    flowLog.logRunFinished(issueNumber, runBudget);
  }
}

// A run that reached its budget ends without a failure: the limit did its job. The next label event or a
// manual run starts again.
main().catch((error: unknown) => {
  if (error instanceof BudgetSpentError) {
    flowLog.logBudgetSpent(error);
    return;
  }
  flowLog.logFailure(error);
  failProcess();
});
