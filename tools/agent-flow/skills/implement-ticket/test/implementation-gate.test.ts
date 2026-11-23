import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkImplementationGate } from '../implementation-gate.ts';
import type { GateInput } from '../implementation-gate.ts';

const plan = { plannedBodySha: 'current', planText: 'plan' };
const approved: GateInput = { bodySha: 'current', plan, planApproved: true, pullRequestNumber: null };

function closedReason(gateInput: GateInput): string {
  const gate = checkImplementationGate(gateInput);
  assert.equal(gate.open, false);
  return gate.open ? '' : gate.reason;
}

test('an approved plan for the current body opens the gate and is handed over', () => {
  assert.deepEqual(checkImplementationGate(approved), { open: true, plan });
});

test('an existing PR closes the gate, even with an approved plan', () => {
  assert.match(closedReason({ ...approved, pullRequestNumber: 7 }), /PR #7 already exists/);
});

test('without a plan the gate is closed', () => {
  assert.match(closedReason({ ...approved, plan: null }), /no plan/);
});

test('an approval of a plan for an older body does not count', () => {
  assert.match(closedReason({ ...approved, bodySha: 'edited' }), /older version of the body/);
});

test('a plan for the current body without the label is not approved', () => {
  assert.match(closedReason({ ...approved, planApproved: false }), /not approved/);
});
