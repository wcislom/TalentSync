// npm --prefix tools/agent-flow run print-body-sha <issue-number>
//
// Prints `body-sha: <hash>`, a hash of the issue body. The plan-ticket skill runs it before it reads the body,
// and later gives the hash to publish-plan. If the body changed while the plan was being written, publish-plan
// refuses to publish, because the plan would describe an older version of the issue.
//
// The skill cannot compute the hash itself, e.g. with `gh ... | sha256sum`: that treats line endings and a
// trailing newline differently from hashIssueBody, and the orchestrator compares hashes made by hashIssueBody
// to decide whether a plan is up to date.

import { runCommand } from '../../shared/command/command-runner.ts';
import { parseIssueNumber } from '../../shared/command/issue-number.ts';
import { getIssue } from '../../shared/github/issues.ts';
import { hashIssueBody } from '../../shared/plan/body-sha.ts';

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2), 'print-body-sha');
  const issue = await getIssue(issueNumber);
  // This is output for the skill, not a log line, so it goes straight to stdout, not through the logger.
  process.stdout.write(`body-sha: ${hashIssueBody(issue.body)}\n`);
}

runCommand('print-body-sha', main);
