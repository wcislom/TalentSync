export function parseIssueNumber(args: string[], command: string): number {
  if (args.length !== 1 || !/^[1-9][0-9]*$/.test(args[0])) {
    throw new Error(`usage: ${command} <issue-number>`);
  }
  return Number(args[0]);
}
