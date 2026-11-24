// npm --prefix tools/agent-flow run print-body-sha <issue-number>
//
// Prints `body-sha: <hash>` of the issue body. The plan-ticket skill runs it before it reads the body and passes the
// hash to publish-plan, which refuses to publish if the body changed while the plan was being written.
//
// The skill cannot compute the hash itself: `gh ... | sha256sum` treats a trailing newline and CRLF differently
// from hashIssueBody, which the orchestrator uses to tell whether a plan is up to date.

import { runCommand } from '../../shared/command/command-runner.ts';
import { parseIssueNumber } from '../../shared/command/issue-number.ts';
import { getIssue } from '../../shared/github/issues.ts';
import { hashIssueBody } from '../../shared/plan/body-sha.ts';

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2), 'print-body-sha');
  const issue = await getIssue(issueNumber);
  // The command's output for the skill, not a log line, so it bypasses the logger.
  process.stdout.write(`body-sha: ${hashIssueBody(issue.body)}\n`);
}

runCommand('print-body-sha', main);
