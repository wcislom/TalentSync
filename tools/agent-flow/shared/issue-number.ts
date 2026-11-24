// PRs share the issue numbering, so a PR number is parsed the same way.
export function parseIssueNumber(args: string[], command: string, argumentName = 'issue-number'): number {
  if (args.length !== 1 || !/^[1-9][0-9]*$/.test(args[0])) {
    throw new Error(`usage: ${command} <${argumentName}>`);
  }
  return Number(args[0]);
}
