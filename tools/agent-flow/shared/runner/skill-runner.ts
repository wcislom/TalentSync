import { query } from '@anthropic-ai/claude-agent-sdk';
import type { SDKResultMessage } from '@anthropic-ai/claude-agent-sdk';
import { config } from '../config/config.ts';
import type { Budget } from '../config/config.ts';
import { format } from '../logging/logger.ts';

export type Session = { id: string; totalCostUsd: number };
export type SkillRun = {
  succeeded: boolean;
  // How the run ended, as the SDK reports it, e.g. success or error_max_budget_usd.
  outcome: string;
  // The run hit its turn or cost limit. The limit worked as intended, so this is not a failure of the flow.
  stoppedByBudget: boolean;
  turns: number;
  stepCostUsd: number;
  durationMs: number;
  // The skill's last message ('' if it stopped early). It is based on issue or PR content, so never log it.
  resultText: string;
  // Tools the skill tried to use but was not allowed to. Only the names: a call's input can hold issue content.
  deniedTools: string[];
  session: Session;
};

// Runs one skill through the Agent SDK, like typing /<skill> <argument> in Claude Code.
// With `resumeFrom`, the skill continues an earlier session and sees what the earlier steps found (plan-ticket
// uses what explain-ticket found). Nothing has to pass through logs, GitHub or the prompt.
// If the skill stops early (budget used up, error), we return the result instead of throwing, so its turns and
// cost are still counted.
export async function runSkill(
  skill: string,
  argument: string,
  budget: Budget,
  resumeFrom?: Session,
): Promise<SkillRun> {
  const abortController = new AbortController();
  const sdkQuery = query({
    // Our skills have `disable-model-invocation: true`: the model cannot start them by itself, only a user can,
    // by typing the slash command. A prompt like "/plan-ticket 35" is handled the same way, so the skill runs.
    prompt: `/${skill} ${argument}`,
    options: {
      cwd: config.repoRoot,
      resume: resumeFrom?.id,
      // Load only the repo's settings: the skills, CLAUDE.md and the permissions in .claude/settings.json.
      // The user's personal settings and skills are not loaded, so the run behaves the same on every machine.
      settingSources: ['project'],
      // Claude Code's built-in skills are loaded anyway. This list stops the model from starting any skill other
      // than this one. It does not block our own slash command above. A resumed session uses this list too, not
      // the list of the session it continues.
      skills: [skill],
      // In a script nobody can answer "may I run this?" questions. So anything that the skill's `allowed-tools`
      // and the repo's rules do not allow is refused at once. Refused calls are listed in the result
      // (permission_denials).
      permissionMode: 'dontAsk',
      // Both limits count only this call, not the earlier turns of a resumed session. num_turns in the result
      // counts the same way.
      maxTurns: budget.maxTurns,
      maxBudgetUsd: budget.maxBudgetUsd,
      abortController,
    },
  });

  let resultMessage: SDKResultMessage | null = null;
  for await (const message of sdkQuery) {
    if (message.type === 'system' && message.subtype === 'init' && !message.slash_commands.includes(skill)) {
      // If the skill does not exist, "/name" reaches the model as plain text and the model makes something up.
      // So we stop before that happens.
      abortController.abort();
      throw new Error(`skill ${skill} is not available in this session`);
    }
    if (message.type === 'result') resultMessage = message;
  }
  if (resultMessage === null) throw new Error(`skill ${skill} ended without a result`);

  // In a resumed session, total_cost_usd usually includes the cost of earlier calls too. That works only if the
  // saved session kept the earlier total. If the new total is lower than the previous one, the earlier cost was
  // not carried over, and the total is the cost of this call only.
  const reportedTotal = resultMessage.total_cost_usd;
  const previousTotal = resumeFrom?.totalCostUsd ?? 0;
  return {
    succeeded: resultMessage.subtype === 'success' && !resultMessage.is_error,
    outcome: resultMessage.is_error ? `${resultMessage.subtype} (error)` : resultMessage.subtype,
    stoppedByBudget: resultMessage.subtype === 'error_max_turns' || resultMessage.subtype === 'error_max_budget_usd',
    turns: resultMessage.num_turns,
    stepCostUsd: reportedTotal >= previousTotal ? reportedTotal - previousTotal : reportedTotal,
    durationMs: resultMessage.duration_ms,
    resultText: resultMessage.subtype === 'success' ? resultMessage.result : '',
    deniedTools: [...new Set(resultMessage.permission_denials.map((denial) => denial.tool_name))],
    session: { id: resultMessage.session_id, totalCostUsd: Math.max(reportedTotal, previousTotal) },
  };
}

// One log line about a skill run, e.g. "done in 4 min 51 s, 16 turns, $0.6083; permission denied: Bash".
export function formatSkillRun(skillRun: SkillRun): string {
  const result = skillRun.succeeded ? 'done' : `stopped (${skillRun.outcome})`;
  const denials = skillRun.deniedTools.length === 0 ? '' : `; permission denied: ${skillRun.deniedTools.join(', ')}`;
  return `${result} in ${format.duration(skillRun.durationMs)}, ${skillRun.turns} turns, `
    + `${format.usd(skillRun.stepCostUsd)}${denials}`;
}
