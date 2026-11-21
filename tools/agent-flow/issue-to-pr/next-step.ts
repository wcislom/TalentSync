import type { LockState } from './lock.ts';
import type { StepName } from './steps.ts';

export type StopReason = 'already running' | 'waiting for approval' | 'done';

export type FlowState = {
  lockState: LockState;
  bodySha: string;
  plannedBodySha: string | null;
  planApproved: boolean;
  pullRequestExists: boolean;
};

export type NextStep =
  | { kind: 'stop'; reason: StopReason }
  | { kind: 'steps'; steps: StepName[]; replaceStaleLock: boolean };

export function decideNextStep(flowState: FlowState, now: Date, lockStaleAfterMinutes: number): NextStep {
  const { lockState, bodySha, plannedBodySha, planApproved, pullRequestExists } = flowState;

  // A label without a `labeled` event cannot be aged; treating it as stale keeps the issue from being stuck,
  // and is safe because the lock only saves work.
  const lockFresh = lockState.held && lockState.heldSince !== null
    && now.getTime() - lockState.heldSince.getTime() < lockStaleAfterMinutes * 60_000;
  if (lockFresh) return { kind: 'stop', reason: 'already running' };

  // Checked before the plan: once the PR exists the flow is finished, even if the body changed afterwards.
  if (pullRequestExists) return { kind: 'stop', reason: 'done' };

  const replaceStaleLock = lockState.held;
  if (plannedBodySha !== bodySha) return { kind: 'steps', steps: ['explain', 'plan'], replaceStaleLock };
  if (!planApproved) return { kind: 'stop', reason: 'waiting for approval' };
  return { kind: 'steps', steps: ['implement'], replaceStaleLock };
}
