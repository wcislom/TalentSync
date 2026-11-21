import assert from 'node:assert/strict';
import { test } from 'node:test';
import { lockState } from '../lock.ts';

const lock = 'agent:running';

test('the lock age is the latest labeled event of the lock label', () => {
  const timeline = [
    { event: 'labeled', labelName: lock, createdAt: '2026-11-20T10:00:00Z' },
    { event: 'unlabeled', labelName: lock, createdAt: '2026-11-20T11:00:00Z' },
    { event: 'labeled', labelName: lock, createdAt: '2026-11-21T09:00:00Z' },
    { event: 'labeled', labelName: 'bug', createdAt: '2026-11-21T10:00:00Z' },
    { event: 'commented', labelName: null, createdAt: '2026-11-21T11:00:00Z' },
  ];
  assert.deepEqual(lockState([lock, 'bug'], timeline, lock),
    { held: true, labeledAt: new Date('2026-11-21T09:00:00Z') });
});

test('without the label there is no lock, whatever the timeline says', () => {
  const timeline = [{ event: 'labeled', labelName: lock, createdAt: '2026-11-21T09:00:00Z' }];
  assert.deepEqual(lockState(['bug'], timeline, lock), { held: false, labeledAt: null });
});

test('a label without a labeled event has no age', () => {
  assert.deepEqual(lockState([lock], [], lock), { held: true, labeledAt: null });
});
