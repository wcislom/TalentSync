import type { LockState } from './lock.ts';
import { StepName } from './steps.ts';

export const NextStepKind = { Stop: 'Stop', Steps: 'Steps' } as const;
export type NextStepKind = (typeof NextStepKind)[keyof typeof NextStepKind];

export const StopReason = {
  AlreadyRunning: 'AlreadyRunning',
  WaitingForApproval: 'WaitingForApproval',
  Done: 'Done',
} as const;
export type StopReason = (typeof StopReason)[keyof typeof StopReason];

export const RunReason = { NoPlan: 'NoPlan', PlanOutOfDate: 'PlanOutOfDate', PlanApproved: 'PlanApproved' } as const;
export type RunReason = (typeof RunReason)[keyof typeof RunReason];

export type FlowState = {
  lockState: LockState;
  bodySha: string;
  plannedBodySha: string | null;
  planApproved: boolean;
  pullRequestNumber: number | null;
};

type Stop = typeof NextStepKind.Stop;

export type NextStep =
  | { kind: Stop; reason: typeof StopReason.AlreadyRunning; heldSince: Date; staleAt: Date }
  | { kind: Stop; reason: typeof StopReason.WaitingForApproval }
  | { kind: Stop; reason: typeof StopReason.Done; pullRequestNumber: number }
  | { kind: typeof NextStepKind.Steps; reason: RunReason; steps: StepName[]; replaceStaleLock: boolean };

export function decideNextStep(flowState: FlowState, now: Date, lockStaleAfterMinutes: number): NextStep {
  const { lockState, bodySha, plannedBodySha, planApproved, pullRequestNumber } = flowState;
  const { held, heldSince } = lockState;

  // If we cannot find when the lock label was added, we cannot tell its age. We treat it as stale, so the issue
  // is not blocked forever. This is safe because the lock only saves work (see lock.ts).
  if (held && heldSince !== null) {
    const staleAt = new Date(heldSince.getTime() + lockStaleAfterMinutes * 60_000);
    if (now < staleAt) return { kind: NextStepKind.Stop, reason: StopReason.AlreadyRunning, heldSince, staleAt };
  }

  // This comes before the plan checks. Once the PR exists, the flow is finished for this issue, even if
  // someone edits the issue body later.
  if (pullRequestNumber !== null) return { kind: NextStepKind.Stop, reason: StopReason.Done, pullRequestNumber };

  const replaceStaleLock = held;
  if (plannedBodySha !== bodySha) {
    const reason = plannedBodySha === null ? RunReason.NoPlan : RunReason.PlanOutOfDate;
    return { kind: NextStepKind.Steps, reason, steps: [StepName.Explain, StepName.Plan], replaceStaleLock };
  }
  if (!planApproved) return { kind: NextStepKind.Stop, reason: StopReason.WaitingForApproval };
  return { kind: NextStepKind.Steps, reason: RunReason.PlanApproved, steps: [StepName.Implement], replaceStaleLock };
}
