import type { Budget } from '../config/config.ts';
import type { SkillRun } from './skill-runner.ts';

// Thrown when a flow run stops because it reached its budget. The flow catches it by type, to log a warning
// instead of an error.
export class BudgetSpentError extends Error {}

// Counts the turns and the cost a flow run has used so far, and what is left of its limit.
export class RunBudget {
  readonly #limit: Budget;
  #turns = 0;
  #costUsd = 0;

  constructor(limit: Budget) {
    this.#limit = limit;
  }

  get turns(): number {
    return this.#turns;
  }

  get costUsd(): number {
    return this.#costUsd;
  }

  record(skillRun: SkillRun): void {
    this.#turns += skillRun.turns;
    this.#costUsd += skillRun.stepCostUsd;
  }

  getRemaining(): Budget {
    return {
      maxTurns: this.#limit.maxTurns - this.#turns,
      maxBudgetUsd: this.#limit.maxBudgetUsd - this.#costUsd,
    };
  }

  isSpent(): boolean {
    const remaining = this.getRemaining();
    return remaining.maxTurns <= 0 || remaining.maxBudgetUsd <= 0;
  }
}
