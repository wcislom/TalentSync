import type { Budget } from '../config/config.ts';
import type { SkillRun } from './skill-runner.ts';

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

  remaining(): Budget {
    return {
      maxTurns: this.#limit.maxTurns - this.#turns,
      maxBudgetUsd: this.#limit.maxBudgetUsd - this.#costUsd,
    };
  }

  isSpent(): boolean {
    const remaining = this.remaining();
    return remaining.maxTurns <= 0 || remaining.maxBudgetUsd <= 0;
  }
}
