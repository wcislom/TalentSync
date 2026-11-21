import type { LockState } from './lock.ts';

export type StepName = 'explain' | 'plan' | 'implement';

export type IssueState = {
  lock: LockState;
  bodySha: string;
  planBodySha: string | null;
  planApproved: boolean;
  pullRequestExists: boolean;
};

export type Decision =
  | { kind: 'stop'; reason: 'already running' | 'waiting for approval' | 'done' }
  | { kind: 'run'; steps: StepName[]; staleLockPresent: boolean };

export function decide(state: IssueState, now: Date, lockStaleAfterMinutes: number): Decision {
  // A label without a `labeled` event cannot be aged; treating it as stale keeps the issue from being stuck,
  // and is safe because the lock only saves work.
  const lockFresh = state.lock.held && state.lock.labeledAt !== null
    && now.getTime() - state.lock.labeledAt.getTime() < lockStaleAfterMinutes * 60_000;
  if (lockFresh) return { kind: 'stop', reason: 'already running' };

  // Checked before the plan: once the PR exists the flow is finished, even if the body changed afterwards.
  if (state.pullRequestExists) return { kind: 'stop', reason: 'done' };

  const staleLockPresent = state.lock.held;

  if (state.planBodySha !== state.bodySha) return { kind: 'run', steps: ['explain', 'plan'], staleLockPresent };
  if (!state.planApproved) return { kind: 'stop', reason: 'waiting for approval' };
  return { kind: 'run', steps: ['implement'], staleLockPresent };
}
