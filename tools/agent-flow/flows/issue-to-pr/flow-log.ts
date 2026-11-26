import { config, getAgentBranch } from '../../shared/config/config.ts';
import type { Budget } from '../../shared/config/config.ts';
import { format, logger } from '../../shared/logging/logger.ts';
import type { BudgetSpentError, RunBudget } from '../../shared/runner/run-budget.ts';
import { formatSkillRun } from '../../shared/runner/skill-runner.ts';
import type { SkillRun } from '../../shared/runner/skill-runner.ts';
import { NextStepKind, RunReason, StopReason } from './next-step.ts';
import type { NextStep } from './next-step.ts';
import type { StepName } from './steps.ts';

const runReasonText: Record<RunReason, string> = {
  [RunReason.NoPlan]: 'no plan',
  [RunReason.PlanOutOfDate]: 'the plan is for an older body',
  [RunReason.PlanApproved]: 'plan approved',
};

function formatStopReason(issueNumber: number, nextStep: NextStep & { kind: typeof NextStepKind.Stop }): string {
  switch (nextStep.reason) {
    case StopReason.NotAssigned:
      return `not assigned: add the label ${config.labels.agentAssigned} to start the flow`;
    case StopReason.UntrustedAuthor:
      return 'the author is not from this repo: copy the issue into a new one to run the flow on it';
    case StopReason.AlreadyRunning:
      return `already running since ${format.utcTime(nextStep.heldSince)} `
        + `(stale after ${format.utcTime(nextStep.staleAt)})`;
    case StopReason.WaitingForApproval:
      return `waiting for approval: the plan matches the body, add the label ${config.labels.planApproved}`;
    case StopReason.Done:
      return `done: PR #${nextStep.pullRequestNumber} from ${getAgentBranch(issueNumber)} exists`;
  }
}

function formatCommand(skill: string, issueNumber: number): string {
  return `/${skill} ${issueNumber}`;
}

export const flowLog = {
  logNextStep(issueNumber: number, nextStep: NextStep): void {
    if (nextStep.kind === NextStepKind.Stop) {
      logger.info(`issue #${issueNumber}: stop, ${formatStopReason(issueNumber, nextStep)}`);
      return;
    }
    logger.info(`issue #${issueNumber}: ${runReasonText[nextStep.reason]} → ${nextStep.steps.join(', ')}`);
  },
  logLockTaken(issueNumber: number, replacedStaleLock: boolean): void {
    const action = replacedStaleLock ? 'stale lock replaced' : 'lock taken';
    logger.info(`issue #${issueNumber}: ${action} (${config.labels.agentRunning})`);
  },
  logStepStarted(stepName: StepName, skill: string, issueNumber: number): void {
    logger.startGroup(`[${stepName}] ${formatCommand(skill, issueNumber)}`);
    logger.info(`[${stepName}] running ${formatCommand(skill, issueNumber)}`);
  },
  logStepStubbed(stepName: StepName, skill: string, issueNumber: number): void {
    logger.endGroup();
    logger.info(`[${stepName}] skipped: ${formatCommand(skill, issueNumber)} is a stub`);
  },
  logStepFinished(
    stepName: StepName, skill: string, issueNumber: number, skillRun: SkillRun, budgetLeft: Budget,
  ): void {
    logger.info(`[${stepName}] ${formatCommand(skill, issueNumber)}: ${formatSkillRun(skillRun)} `
      + `(budget left: ${budgetLeft.maxTurns} turns, ${format.usd(budgetLeft.maxBudgetUsd)})`);
    logger.endGroup();
  },
  logLockReleased(issueNumber: number): void {
    logger.info(`issue #${issueNumber}: lock released (${config.labels.agentRunning})`);
  },
  logLockReleaseFailed(issueNumber: number, cause: unknown): void {
    logger.error(`issue #${issueNumber}: failed to release the lock (${config.labels.agentRunning}), `
      + `it goes stale after ${config.lockStaleAfterMinutes} min: ${format.errorMessage(cause)}`);
  },
  logRunFinished(issueNumber: number, runBudget: RunBudget): void {
    logger.info(`issue #${issueNumber}: total ${runBudget.turns} turns, ${format.usd(runBudget.costUsd)}`);
  },
  logBudgetSpent(cause: BudgetSpentError): void {
    logger.warning(`issue-to-pr stopped by its budget: ${cause.message}`);
  },
  logFailure(cause: unknown): void {
    logger.error(`issue-to-pr failed: ${format.errorMessage(cause)}`);
  },
};
