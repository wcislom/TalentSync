// All log lines go through this file. In GitHub Actions (#47) the logs are public, so a log line may hold only
// numbers and names: issue numbers, step and skill names, counts, cost. Never issue text, comments or a plan,
// because anyone can write anything there.

const inGitHubActions = process.env.GITHUB_ACTIONS === 'true';
let groupOpen = false;

// GitHub Actions reads special log lines like "::error::message". A newline ends such a line and "%" starts an
// escape code, so both are encoded.
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
  // In GitHub Actions a group is a part of the log you can fold. Locally the lines are just printed.
  startGroup(title: string): void {
    endGroup();
    if (!inGitHubActions) return;
    console.log(`::group::${escapeWorkflowCommand(title)}`);
    groupOpen = true;
  },
  endGroup,
  // For a stop that is expected, like a run that reached its budget. In GitHub Actions it is shown in the job
  // summary like an error, but in yellow.
  warning(line: string): void {
    endGroup();
    console.error(inGitHubActions ? `::warning::${escapeWorkflowCommand(line)}` : line);
  },
  // In GitHub Actions an ::error:: line is also shown in the job summary, so it is easy to find.
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
