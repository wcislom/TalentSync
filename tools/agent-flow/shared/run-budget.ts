import type { Budget } from './config.ts';
import type { SkillRun } from './skill-runner.ts';

// What one orchestrator run has spent so far, against the limit for the whole run.
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
