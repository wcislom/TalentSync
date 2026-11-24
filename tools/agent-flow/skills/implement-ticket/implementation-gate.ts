import { config } from '../../shared/config/config.ts';
import type { Plan } from '../../shared/plan/plan-marker.ts';

// What the gate reads from the issue. The approval is the issue's label, not part of the plan comment.
export type GateInput = {
  bodySha: string;
  plan: Plan | null;
  planApproved: boolean;
  pullRequestNumber: number | null;
};

export type Gate = { open: true; plan: Plan } | { open: false; reason: string };

// The orchestrator makes the same decision before it runs the skill (next-step.ts); a skill run by hand
// as /implement-ticket has only this gate between an unapproved plan and the code.
export function checkImplementationGate(gateInput: GateInput): Gate {
  const { bodySha, plan, planApproved, pullRequestNumber } = gateInput;
  if (pullRequestNumber !== null) {
    return { open: false, reason: `PR #${pullRequestNumber} already exists: nothing to implement` };
  }
  if (plan === null) return { open: false, reason: 'no plan from a trusted author: run /plan-ticket first' };
  if (plan.plannedBodySha !== bodySha) {
    return { open: false, reason: 'the plan is for an older version of the body: plan again' };
  }
  if (!planApproved) {
    return { open: false, reason: `the plan is not approved: the label ${config.labels.planApproved} is missing` };
  }
  return { open: true, plan };
}
