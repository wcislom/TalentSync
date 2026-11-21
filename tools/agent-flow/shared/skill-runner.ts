import { query } from '@anthropic-ai/claude-agent-sdk';
import type { SDKResultMessage } from '@anthropic-ai/claude-agent-sdk';
import { config } from './config.ts';
import type { Budget } from './config.ts';

export type Session = { id: string; totalCostUsd: number };
export type SkillRun = {
  succeeded: boolean;
  // The SDK result subtype, e.g. success or error_max_budget_usd.
  outcome: string;
  turns: number;
  stepCostUsd: number;
  durationMs: number;
  // Tool names only: the denied calls' inputs can hold issue content.
  deniedTools: string[];
  session: Session;
};

// With `resumeFrom`, the skill runs in that session and sees the earlier steps' output (plan-ticket builds on
// explain-ticket's) without it passing through logs, GitHub or a prompt argument.
// A skill that stops early (budget, error) is returned, not thrown, so its turns and cost still count.
export async function runSkill(
  skill: string,
  argument: string,
  budget: Budget,
  resumeFrom?: Session,
): Promise<SkillRun> {
  const abortController = new AbortController();
  const sdkQuery = query({
    // A string prompt goes through slash-command dispatch (unless `verbatimPrompts`), which is how a user runs a
    // skill with `disable-model-invocation: true`; the model itself could not invoke it through the Skill tool.
    prompt: `/${skill} ${argument}`,
    options: {
      cwd: config.repoRoot,
      resume: resumeFrom?.id,
      // Project only: the skill, CLAUDE.md and the permission rules in .claude/settings.json, the same on
      // every machine. The user's own settings and skills are not loaded.
      settingSources: ['project'],
      // Built-in skills load even with project settings only. The allowlist stops the model from invoking them
      // through the Skill tool; it does not filter slash-command dispatch, and init still lists every skill.
      // A resumed session takes the allowlist from these options, not from the session it resumes.
      skills: [skill],
      // No one answers a permission prompt in a script: anything the skill's `allowed-tools` and the project
      // rules do not allow is denied, and shows up in the result's permission_denials.
      permissionMode: 'dontAsk',
      // Both count only this call, not the resumed session's earlier turns (num_turns in the result does too).
      maxTurns: budget.maxTurns,
      maxBudgetUsd: budget.maxBudgetUsd,
      abortController,
    },
  });

  let resultMessage: SDKResultMessage | null = null;
  for await (const message of sdkQuery) {
    if (message.type === 'system' && message.subtype === 'init' && !message.slash_commands.includes(skill)) {
      // Without this check an unknown command reaches the model as plain text and it improvises.
      abortController.abort();
      throw new Error(`skill ${skill} is not available in this session`);
    }
    if (message.type === 'result') resultMessage = message;
  }
  if (resultMessage === null) throw new Error(`skill ${skill} ended without a result`);

  // total_cost_usd of a resumed session includes the earlier calls, but only when its transcript saved their total;
  // a total below the previous one means it was not carried over and covers this call alone.
  const reportedTotal = resultMessage.total_cost_usd;
  const previousTotal = resumeFrom?.totalCostUsd ?? 0;
  return {
    succeeded: resultMessage.subtype === 'success' && !resultMessage.is_error,
    outcome: resultMessage.is_error ? `${resultMessage.subtype} (error)` : resultMessage.subtype,
    turns: resultMessage.num_turns,
    stepCostUsd: reportedTotal >= previousTotal ? reportedTotal - previousTotal : reportedTotal,
    durationMs: resultMessage.duration_ms,
    deniedTools: [...new Set(resultMessage.permission_denials.map((denial) => denial.tool_name))],
    session: { id: resultMessage.session_id, totalCostUsd: Math.max(reportedTotal, previousTotal) },
  };
}
