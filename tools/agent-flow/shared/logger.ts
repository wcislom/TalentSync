// CI logs are public (#47). Every method takes issue numbers, step and skill names, and numbers; none takes free
// text, so the issue body or a plan cannot reach the log by accident. Error messages are ours or gh's stderr.

const inGitHubActions = process.env.GITHUB_ACTIONS === 'true';
let groupOpen = false;

// Workflow commands end at a newline, and `%` starts an escape.
export function escapeWorkflowCommand(text: string): string {
  return text.replaceAll('%', '%25').replaceAll('\r', '%0D').replaceAll('\n', '%0A');
}

function info(line: string): void {
  console.log(line);
}

// In Actions each step is a collapsible group; locally the step name prefixes its lines instead.
function startGroup(title: string): void {
  endGroup();
  if (!inGitHubActions) return;
  console.log(`::group::${escapeWorkflowCommand(title)}`);
  groupOpen = true;
}

function endGroup(): void {
  if (!groupOpen) return;
  console.log('::endgroup::');
  groupOpen = false;
}

function error(line: string): void {
  endGroup();
  // An ::error:: line also shows up in the job summary.
  console.error(inGitHubActions ? `::error::${escapeWorkflowCommand(line)}` : line);
}

function usd(amount: number): string {
  return `$${amount.toFixed(4)}`;
}

function messageOf(cause: unknown): string {
  return cause instanceof Error ? cause.message : String(cause);
}

export const logger = {
  stopped(issueNumber: number, reason: string): void {
    info(`issue #${issueNumber}: stop, ${reason}`);
  },
  stepsPlanned(issueNumber: number, steps: readonly string[]): void {
    info(`issue #${issueNumber}: run ${steps.join(', ')}`);
  },
  stepStarted(step: string, skill: string, issueNumber: number): void {
    startGroup(`[${step}] /${skill} ${issueNumber}`);
    info(`[${step}] running /${skill} ${issueNumber}`);
  },
  stepStubbed(step: string, skill: string, issueNumber: number): void {
    endGroup();
    info(`[${step}] would run /${skill} ${issueNumber}`);
  },
  skillFinished(skill: string, outcome: string, turns: number, costUsd: number, permissionDenials: number): void {
    info(`[${skill}] ${outcome}: ${turns} turns, ${usd(costUsd)}, ${permissionDenials} permission denials`);
  },
  runFinished(issueNumber: number, turns: number, costUsd: number): void {
    endGroup();
    info(`issue #${issueNumber}: ${turns} turns, ${usd(costUsd)} in total`);
  },
  lockReleaseFailed(issueNumber: number, cause: unknown): void {
    error(`issue #${issueNumber}: failed to release the lock: ${messageOf(cause)}`);
  },
  failed(cause: unknown): void {
    error(`issue-to-pr failed: ${messageOf(cause)}`);
  },
};
