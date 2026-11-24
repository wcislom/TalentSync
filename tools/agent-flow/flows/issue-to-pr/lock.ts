import { addLabel, removeLabel } from '../../shared/github/issues.ts';
import type { TimelineEvent } from '../../shared/github/issues.ts';

// The lock is the label agent:running on the issue. It stops a second run from starting work that is already
// in progress, to save time and money.
// It is not a perfect lock: reading the labels and adding the label are two separate requests. Two runs that
// start at the same moment can both see no label and both continue.
// That is still safe, because every step can run twice without damage: the plan comment is edited in place,
// the push is never forced, and an existing PR stops the flow.

export type LockState = { held: boolean; heldSince: Date | null };

export function readLockState(labels: string[], timelineEvents: TimelineEvent[], lockLabel: string): LockState {
  const held = labels.includes(lockLabel);
  return { held, heldSince: held ? lastLabeledAt(timelineEvents, lockLabel) : null };
}

function lastLabeledAt(timelineEvents: TimelineEvent[], label: string): Date | null {
  let latest: Date | null = null;
  for (const event of timelineEvents) {
    if (event.event !== 'labeled' || event.labelName !== label) continue;
    const labeledAt = new Date(event.createdAt);
    if (latest === null || labeledAt > latest) latest = labeledAt;
  }
  return latest;
}

// The lock's age is the time of the label's `labeled` event in the issue timeline. When we take over a stale
// lock, we remove the label first: adding it again creates a new `labeled` event, so the lock gets a new age.
export async function acquireLock(issueNumber: number, lockLabel: string, replaceStaleLock: boolean): Promise<void> {
  if (replaceStaleLock) await removeLabel(issueNumber, lockLabel);
  await addLabel(issueNumber, lockLabel);
}

export async function releaseLock(issueNumber: number, lockLabel: string): Promise<void> {
  await removeLabel(issueNumber, lockLabel);
}
