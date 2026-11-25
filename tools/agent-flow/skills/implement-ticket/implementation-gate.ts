import { config } from '../../shared/config/config.ts';
import type { Plan } from '../../shared/plan/plan-marker.ts';

// What the gate needs to know about the issue. The approval is a label on the issue (agent:plan-approved), not part of
// the plan comment.
export type GateInput = {
  bodySha: string;
  plan: Plan | null;
  planApproved: boolean;
  pullRequestNumber: number | null;
};

export type Gate = { open: true; plan: Plan } | { open: false; reason: string };

// Decides whether the agent may start writing code for this issue. The orchestrator checks the same thing
// before it runs the skill (next-step.ts). But someone can also run /implement-ticket by hand, and then this gate
// is the only thing that stops work on a plan nobody approved.
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
