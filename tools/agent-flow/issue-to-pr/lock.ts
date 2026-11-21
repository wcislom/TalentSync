import type { TimelineEvent } from '../shared/github-client.ts';
import { addLabel, removeLabel } from '../shared/github-client.ts';

// The lock only saves work, it does not guarantee mutual exclusion: reading the label and adding it are two
// separate requests, so two runs that start together can both see no label and both proceed.
// Correctness comes from idempotent effects (one plan comment edited in place, no force push, an existing PR = done).

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

// A stale label is removed first so that adding it again writes a new `labeled` event and resets its age.
export async function acquireLock(issueNumber: number, lockLabel: string, replaceStaleLock: boolean): Promise<void> {
  if (replaceStaleLock) await removeLabel(issueNumber, lockLabel);
  await addLabel(issueNumber, lockLabel);
}

export async function releaseLock(issueNumber: number, lockLabel: string): Promise<void> {
  await removeLabel(issueNumber, lockLabel);
}
