import assert from 'node:assert/strict';
import { test } from 'node:test';
import { RunBudget } from '../run-budget.ts';
import type { SkillRun } from '../skill-runner.ts';

const limit = { maxTurns: 80, maxBudgetUsd: 5 };

function skillRun(turns: number, stepCostUsd: number): SkillRun {
  return {
    succeeded: true,
    outcome: 'success',
    turns,
    stepCostUsd,
    durationMs: 1000,
    resultText: '',
    deniedTools: [],
    session: { id: 'session', totalCostUsd: stepCostUsd },
  };
}

test('a new budget has spent nothing and has the whole limit left', () => {
  const runBudget = new RunBudget(limit);
  assert.equal(runBudget.turns, 0);
  assert.equal(runBudget.costUsd, 0);
  assert.deepEqual(runBudget.remaining(), limit);
  assert.equal(runBudget.isSpent(), false);
});

test('recorded skill runs add up and reduce what is left', () => {
  const runBudget = new RunBudget(limit);
  runBudget.record(skillRun(10, 0.5));
  runBudget.record(skillRun(5, 0.25));
  assert.equal(runBudget.turns, 15);
  assert.equal(runBudget.costUsd, 0.75);
  assert.deepEqual(runBudget.remaining(), { maxTurns: 65, maxBudgetUsd: 4.25 });
});

test('the budget is spent when either turns or cost reach the limit', () => {
  const turnsSpent = new RunBudget(limit);
  turnsSpent.record(skillRun(80, 0.5));
  assert.equal(turnsSpent.isSpent(), true);

  const costSpent = new RunBudget(limit);
  costSpent.record(skillRun(10, 5));
  assert.equal(costSpent.isSpent(), true);
});
