import { query } from '@anthropic-ai/claude-agent-sdk';
import type { SDKResultMessage } from '@anthropic-ai/claude-agent-sdk';
import { config } from './config.ts';
import type { Budget } from './config.ts';

// costUsd is the session's running total, which a resumed session's results carry forward.
export type Session = { id: string; costUsd: number };
export type SkillRun = { turns: number; costUsd: number; session: Session };

// Logs only the skill name, turns and cost: the conversation is derived from the issue body, and CI logs are public.
// With `previous`, the skill runs in that session and sees the earlier steps' output (plan-ticket builds on
// explain-ticket's) without it passing through logs, GitHub or a prompt argument.
export async function runSkill(skill: string, argument: string, budget: Budget, previous?: Session): Promise<SkillRun> {
  const abortController = new AbortController();
  const session = query({
    // A string prompt goes through slash-command dispatch (unless `verbatimPrompts`), which is how a user runs a
    // skill with `disable-model-invocation: true`; the model itself could not invoke it through the Skill tool.
    prompt: `/${skill} ${argument}`,
    options: {
      cwd: config.repoRoot,
      resume: previous?.id,
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
  for await (const message of session) {
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
  const sessionCostUsd = resultMessage.total_cost_usd;
  const previousCostUsd = previous?.costUsd ?? 0;
  const run: SkillRun = {
    turns: resultMessage.num_turns,
    costUsd: sessionCostUsd >= previousCostUsd ? sessionCostUsd - previousCostUsd : sessionCostUsd,
    session: { id: resultMessage.session_id, costUsd: Math.max(sessionCostUsd, previousCostUsd) },
  };
  console.log(`[${skill}] ${resultMessage.subtype}: ${run.turns} turns, $${run.costUsd.toFixed(4)}, `
    + `${resultMessage.permission_denials.length} permission denials`);

  if (resultMessage.subtype !== 'success' || resultMessage.is_error) {
    throw new Error(`skill ${skill} stopped: ${resultMessage.subtype}${resultMessage.is_error ? ' (error)' : ''}`);
  }
  return run;
}
