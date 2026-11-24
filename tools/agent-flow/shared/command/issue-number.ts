// Reads the one number a command takes, e.g. `publish-pr 42`. GitHub uses one number series for issues and
// PRs, so a PR number is checked the same way.
export function parseIssueNumber(args: string[], command: string, argumentName = 'issue-number'): number {
  if (args.length !== 1 || !/^[1-9][0-9]*$/.test(args[0])) {
    throw new Error(`usage: ${command} <${argumentName}>`);
  }
  return Number(args[0]);
}
