import assert from 'node:assert/strict';
import { test } from 'node:test';
import { decideNextStep } from '../next-step.ts';
import type { FlowState } from '../next-step.ts';

const now = new Date('2026-11-21T12:00:00Z');
const staleAfterMinutes = 60;
const minutesAgo = (minutes: number) => new Date(now.getTime() - minutes * 60_000);

const noPlan: FlowState = {
  lockState: { held: false, heldSince: null },
  bodySha: 'current',
  plannedBodySha: null,
  planApproved: false,
  pullRequestExists: false,
};
const currentPlan: FlowState = { ...noPlan, plannedBodySha: 'current' };

test('a lock younger than the limit stops the run', () => {
  const state = { ...currentPlan, planApproved: true, lockState: { held: true, heldSince: minutesAgo(59) } };
  assert.deepEqual(decideNextStep(state, now, staleAfterMinutes), { kind: 'stop', reason: 'already running' });
});

test('a lock older than the limit is stale and the run goes on', () => {
  const state = { ...noPlan, lockState: { held: true, heldSince: minutesAgo(60) } };
  assert.deepEqual(decideNextStep(state, now, staleAfterMinutes),
    { kind: 'steps', steps: ['explain', 'plan'], replaceStaleLock: true });
});

test('a lock label without a labeled event is treated as stale', () => {
  const state = { ...noPlan, lockState: { held: true, heldSince: null } };
  assert.deepEqual(decideNextStep(state, now, staleAfterMinutes),
    { kind: 'steps', steps: ['explain', 'plan'], replaceStaleLock: true });
});

test('no plan comment runs explain and plan', () => {
  assert.deepEqual(decideNextStep(noPlan, now, staleAfterMinutes),
    { kind: 'steps', steps: ['explain', 'plan'], replaceStaleLock: false });
});

test('a plan for an older body runs explain and plan, even when approved', () => {
  const state = { ...noPlan, plannedBodySha: 'older', planApproved: true };
  assert.deepEqual(decideNextStep(state, now, staleAfterMinutes),
    { kind: 'steps', steps: ['explain', 'plan'], replaceStaleLock: false });
});

test('a current plan without approval waits for approval', () => {
  assert.deepEqual(decideNextStep(currentPlan, now, staleAfterMinutes), { kind: 'stop', reason: 'waiting for approval' });
});

test('an approved current plan without a PR runs implement', () => {
  const state = { ...currentPlan, planApproved: true };
  assert.deepEqual(decideNextStep(state, now, staleAfterMinutes),
    { kind: 'steps', steps: ['implement'], replaceStaleLock: false });
});

test('an existing PR is done', () => {
  const state = { ...currentPlan, planApproved: true, pullRequestExists: true };
  assert.deepEqual(decideNextStep(state, now, staleAfterMinutes), { kind: 'stop', reason: 'done' });
});

test('an existing PR is done even when the plan is out of date', () => {
  const state = { ...noPlan, plannedBodySha: 'older', pullRequestExists: true };
  assert.deepEqual(decideNextStep(state, now, staleAfterMinutes), { kind: 'stop', reason: 'done' });
});
