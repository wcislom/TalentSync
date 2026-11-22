export const StepName = { Explain: 'Explain', Plan: 'Plan', Implement: 'Implement' } as const;
export type StepName = (typeof StepName)[keyof typeof StepName];

export type Step = { skill: string; stub: boolean };

// A stub only logs what it would run. #44 and #42 drop the flag from their entry.
export const steps: Record<StepName, Step> = {
  [StepName.Explain]: { skill: 'explain-ticket', stub: false },
  [StepName.Plan]: { skill: 'plan-ticket', stub: false },
  [StepName.Implement]: { skill: 'implement-ticket', stub: true },
};
