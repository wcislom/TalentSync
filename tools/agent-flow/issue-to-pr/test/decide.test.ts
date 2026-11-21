import assert from 'node:assert/strict';
import { test } from 'node:test';
import { decide } from '../decide.ts';
import type { IssueState } from '../decide.ts';

const now = new Date('2026-11-21T12:00:00Z');
const staleAfterMinutes = 60;
const minutesAgo = (minutes: number) => new Date(now.getTime() - minutes * 60_000);

const noPlan: IssueState = {
  lock: { held: false, labeledAt: null },
  bodySha: 'current',
  planBodySha: null,
  planApproved: false,
  pullRequestExists: false,
};
const currentPlan: IssueState = { ...noPlan, planBodySha: 'current' };

test('a lock younger than the limit stops the run', () => {
  const state = { ...currentPlan, planApproved: true, lock: { held: true, labeledAt: minutesAgo(59) } };
  assert.deepEqual(decide(state, now, staleAfterMinutes), { kind: 'stop', reason: 'already running' });
});

test('a lock older than the limit is stale and the run goes on', () => {
  const state = { ...noPlan, lock: { held: true, labeledAt: minutesAgo(60) } };
  assert.deepEqual(decide(state, now, staleAfterMinutes),
    { kind: 'run', steps: ['explain', 'plan'], staleLockPresent: true });
});

test('a lock label without a labeled event is treated as stale', () => {
  const state = { ...noPlan, lock: { held: true, labeledAt: null } };
  assert.deepEqual(decide(state, now, staleAfterMinutes),
    { kind: 'run', steps: ['explain', 'plan'], staleLockPresent: true });
});

test('no plan comment runs explain and plan', () => {
  assert.deepEqual(decide(noPlan, now, staleAfterMinutes),
    { kind: 'run', steps: ['explain', 'plan'], staleLockPresent: false });
});

test('a plan for an older body runs explain and plan, even when approved', () => {
  const state = { ...noPlan, planBodySha: 'older', planApproved: true };
  assert.deepEqual(decide(state, now, staleAfterMinutes),
    { kind: 'run', steps: ['explain', 'plan'], staleLockPresent: false });
});

test('a current plan without approval waits for approval', () => {
  assert.deepEqual(decide(currentPlan, now, staleAfterMinutes), { kind: 'stop', reason: 'waiting for approval' });
});

test('an approved current plan without a PR runs implement', () => {
  const state = { ...currentPlan, planApproved: true };
  assert.deepEqual(decide(state, now, staleAfterMinutes),
    { kind: 'run', steps: ['implement'], staleLockPresent: false });
});

test('an existing PR is done', () => {
  const state = { ...currentPlan, planApproved: true, pullRequestExists: true };
  assert.deepEqual(decide(state, now, staleAfterMinutes), { kind: 'stop', reason: 'done' });
});

test('an existing PR is done even when the plan is out of date', () => {
  const state = { ...noPlan, planBodySha: 'older', pullRequestExists: true };
  assert.deepEqual(decide(state, now, staleAfterMinutes), { kind: 'stop', reason: 'done' });
});
