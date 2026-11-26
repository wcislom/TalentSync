import assert from 'node:assert/strict';
import { test } from 'node:test';
import { decideNextStep, NextStepKind, RunReason, StopReason } from '../next-step.ts';
import type { FlowState } from '../next-step.ts';
import { StepName } from '../steps.ts';

const now = new Date('2026-11-21T12:00:00Z');
const staleAfterMinutes = 60;
const minutesAgo = (minutes: number) => new Date(now.getTime() - minutes * 60_000);

const noPlan: FlowState = {
  assigned: true,
  authorTrusted: true,
  lockState: { held: false, heldSince: null },
  bodySha: 'current',
  plannedBodySha: null,
  planApproved: false,
  pullRequestNumber: null,
};
const currentPlan: FlowState = { ...noPlan, plannedBodySha: 'current' };
const explainAndPlan = [StepName.Explain, StepName.Plan];

test('an issue without agent:assigned stops the run, even with an approved plan', () => {
  const flowState = { ...currentPlan, planApproved: true, assigned: false };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes),
    { kind: NextStepKind.Stop, reason: StopReason.NotAssigned });
});

test('an issue from an author outside the repo stops the run, even when assigned', () => {
  const flowState = { ...noPlan, authorTrusted: false };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes),
    { kind: NextStepKind.Stop, reason: StopReason.UntrustedAuthor });
});

test('a lock younger than the limit stops the run, with when it goes stale', () => {
  const flowState = { ...currentPlan, planApproved: true, lockState: { held: true, heldSince: minutesAgo(59) } };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes), {
    kind: NextStepKind.Stop,
    reason: StopReason.AlreadyRunning,
    heldSince: minutesAgo(59),
    staleAt: new Date('2026-11-21T12:01:00Z'),
  });
});

test('a lock older than the limit is stale and the run goes on', () => {
  const flowState = { ...noPlan, lockState: { held: true, heldSince: minutesAgo(60) } };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes),
    { kind: NextStepKind.Steps, reason: RunReason.NoPlan, steps: explainAndPlan, replaceStaleLock: true });
});

test('a lock label without a labeled event is treated as stale', () => {
  const flowState = { ...noPlan, lockState: { held: true, heldSince: null } };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes),
    { kind: NextStepKind.Steps, reason: RunReason.NoPlan, steps: explainAndPlan, replaceStaleLock: true });
});

test('no plan comment runs explain and plan', () => {
  assert.deepEqual(decideNextStep(noPlan, now, staleAfterMinutes),
    { kind: NextStepKind.Steps, reason: RunReason.NoPlan, steps: explainAndPlan, replaceStaleLock: false });
});

test('a plan for an older body runs explain and plan, even when approved', () => {
  const flowState = { ...noPlan, plannedBodySha: 'older', planApproved: true };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes),
    { kind: NextStepKind.Steps, reason: RunReason.PlanOutOfDate, steps: explainAndPlan, replaceStaleLock: false });
});

test('a current plan without approval waits for approval', () => {
  assert.deepEqual(decideNextStep(currentPlan, now, staleAfterMinutes),
    { kind: NextStepKind.Stop, reason: StopReason.WaitingForApproval });
});

test('an approved current plan without a PR runs implement', () => {
  const flowState = { ...currentPlan, planApproved: true };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes), {
    kind: NextStepKind.Steps,
    reason: RunReason.PlanApproved,
    steps: [StepName.Implement],
    replaceStaleLock: false,
  });
});

test('an existing PR is done', () => {
  const flowState = { ...currentPlan, planApproved: true, pullRequestNumber: 61 };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes),
    { kind: NextStepKind.Stop, reason: StopReason.Done, pullRequestNumber: 61 });
});

test('an existing PR is done even when the plan is out of date', () => {
  const flowState = { ...noPlan, plannedBodySha: 'older', pullRequestNumber: 61 };
  assert.deepEqual(decideNextStep(flowState, now, staleAfterMinutes),
    { kind: NextStepKind.Stop, reason: StopReason.Done, pullRequestNumber: 61 });
});
