import { config, getAgentBranch } from '../../shared/config.ts';
import type { Budget } from '../../shared/config.ts';
import { format, logger } from '../../shared/logger.ts';
import type { RunBudget } from '../../shared/run-budget.ts';
import type { SkillRun } from '../../shared/skill-runner.ts';
import { NextStepKind, RunReason, StopReason } from './next-step.ts';
import type { NextStep } from './next-step.ts';
import type { StepName } from './steps.ts';

const runReasonText: Record<RunReason, string> = {
  [RunReason.NoPlan]: 'no plan',
  [RunReason.PlanOutOfDate]: 'the plan is for an older body',
  [RunReason.PlanApproved]: 'plan approved',
};

function stopText(issueNumber: number, nextStep: NextStep & { kind: typeof NextStepKind.Stop }): string {
  switch (nextStep.reason) {
    case StopReason.AlreadyRunning:
      return `already running since ${format.utcTime(nextStep.heldSince)} `
        + `(stale after ${format.utcTime(nextStep.staleAt)})`;
    case StopReason.WaitingForApproval:
      return `waiting for approval: the plan matches the body, add the label ${config.labels.planApproved}`;
    case StopReason.Done:
      return `done: PR #${nextStep.pullRequestNumber} from ${getAgentBranch(issueNumber)} exists`;
  }
}

function command(skill: string, issueNumber: number): string {
  return `/${skill} ${issueNumber}`;
}

export const flowLog = {
  nextStep(issueNumber: number, nextStep: NextStep): void {
    if (nextStep.kind === NextStepKind.Stop) {
      logger.info(`issue #${issueNumber}: stop, ${stopText(issueNumber, nextStep)}`);
      return;
    }
    logger.info(`issue #${issueNumber}: ${runReasonText[nextStep.reason]} → ${nextStep.steps.join(', ')}`);
  },
  lockTaken(issueNumber: number, replacedStaleLock: boolean): void {
    const action = replacedStaleLock ? 'stale lock replaced' : 'lock taken';
    logger.info(`issue #${issueNumber}: ${action} (${config.labels.agentRunning})`);
  },
  stepStarted(stepName: StepName, skill: string, issueNumber: number): void {
    logger.startGroup(`[${stepName}] ${command(skill, issueNumber)}`);
    logger.info(`[${stepName}] running ${command(skill, issueNumber)}`);
  },
  stepStubbed(stepName: StepName, skill: string, issueNumber: number): void {
    logger.endGroup();
    logger.info(`[${stepName}] skipped: ${command(skill, issueNumber)} is a stub`);
  },
  stepFinished(stepName: StepName, skill: string, issueNumber: number, skillRun: SkillRun, budgetLeft: Budget): void {
    const result = skillRun.succeeded ? 'done' : `stopped (${skillRun.outcome})`;
    const denials = skillRun.deniedTools.length === 0 ? '' : `; permission denied: ${skillRun.deniedTools.join(', ')}`;
    logger.info(`[${stepName}] ${command(skill, issueNumber)}: ${result} in ${format.duration(skillRun.durationMs)}, `
      + `${skillRun.turns} turns, ${format.usd(skillRun.stepCostUsd)} `
      + `(budget left: ${budgetLeft.maxTurns} turns, ${format.usd(budgetLeft.maxBudgetUsd)})${denials}`);
    logger.endGroup();
  },
  lockReleased(issueNumber: number): void {
    logger.info(`issue #${issueNumber}: lock released (${config.labels.agentRunning})`);
  },
  lockReleaseFailed(issueNumber: number, cause: unknown): void {
    logger.error(`issue #${issueNumber}: failed to release the lock (${config.labels.agentRunning}), `
      + `it goes stale after ${config.lockStaleAfterMinutes} min: ${format.errorMessage(cause)}`);
  },
  runFinished(issueNumber: number, runBudget: RunBudget): void {
    logger.info(`issue #${issueNumber}: total ${runBudget.turns} turns, ${format.usd(runBudget.costUsd)}`);
  },
  failed(cause: unknown): void {
    logger.error(`issue-to-pr failed: ${format.errorMessage(cause)}`);
  },
};
