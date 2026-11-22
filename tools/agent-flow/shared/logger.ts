// The only place that writes log lines. CI logs are public (#47): callers build lines from issue numbers,
// step and skill names, and numbers only, never from issue or comment text, so the body or a plan cannot leak.

const inGitHubActions = process.env.GITHUB_ACTIONS === 'true';
let groupOpen = false;

// Workflow commands end at a newline, and `%` starts an escape.
export function escapeWorkflowCommand(text: string): string {
  return text.replaceAll('%', '%25').replaceAll('\r', '%0D').replaceAll('\n', '%0A');
}

function endGroup(): void {
  if (!groupOpen) return;
  console.log('::endgroup::');
  groupOpen = false;
}

export const logger = {
  info(line: string): void {
    console.log(line);
  },
  // In Actions a group is a collapsible section; locally the lines simply follow each other.
  startGroup(title: string): void {
    endGroup();
    if (!inGitHubActions) return;
    console.log(`::group::${escapeWorkflowCommand(title)}`);
    groupOpen = true;
  },
  endGroup,
  // An ::error:: line also shows up in the job summary.
  error(line: string): void {
    endGroup();
    console.error(inGitHubActions ? `::error::${escapeWorkflowCommand(line)}` : line);
  },
};

export const format = {
  usd(amount: number): string {
    return `$${amount.toFixed(4)}`;
  },
  duration(milliseconds: number): string {
    const seconds = Math.round(milliseconds / 1000);
    return seconds < 60 ? `${seconds} s` : `${Math.floor(seconds / 60)} min ${seconds % 60} s`;
  },
  utcTime(date: Date): string {
    return `${date.toISOString().slice(11, 16)} UTC`;
  },
  errorMessage(cause: unknown): string {
    return cause instanceof Error ? cause.message : String(cause);
  },
};
