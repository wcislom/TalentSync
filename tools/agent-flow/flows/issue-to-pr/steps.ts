export const StepName = { Explain: 'Explain', Plan: 'Plan', Implement: 'Implement' } as const;
export type StepName = (typeof StepName)[keyof typeof StepName];

export type Step = { skill: string; stub: boolean };

// A step marked as a stub does not run its skill: it only logs which skill it would run.
// We used this while the skill for a step did not exist yet.
export const steps: Record<StepName, Step> = {
  [StepName.Explain]: { skill: 'explain-ticket', stub: false },
  [StepName.Plan]: { skill: 'plan-ticket', stub: false },
  [StepName.Implement]: { skill: 'implement-ticket', stub: false },
};
