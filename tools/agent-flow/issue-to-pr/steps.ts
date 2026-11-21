export type StepName = 'explain' | 'plan' | 'implement';
export type Step = { skill: string; stub: boolean };

// A stub only logs what it would run. #44 and #42 drop the flag from their entry.
export const steps: Record<StepName, Step> = {
  explain: { skill: 'explain-ticket', stub: false },
  plan: { skill: 'plan-ticket', stub: true },
  implement: { skill: 'implement-ticket', stub: true },
};
