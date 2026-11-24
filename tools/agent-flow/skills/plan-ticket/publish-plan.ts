// npm --prefix tools/agent-flow run publish-plan <issue-number> <body-sha> < plan.md
//
// Publishes the plan-ticket skill's plan as the issue's one plan comment. <body-sha> comes from print-body-sha,
// run before the skill read the body. In order:
//   1. Refuse if the body changed since then: the plan would be for an older version of the issue.
//   2. Refuse if the gh user is not a trusted author: the orchestrator would ignore the plan and plan again.
//   3. Remove plan-approved, so an approval always belongs to the plan the approver read.
//   4. Edit the existing plan comment, or create one if there is none. Other comments are never touched.

import { runCommand } from '../../shared/command/command-runner.ts';
import { config } from '../../shared/config/config.ts';
import { getCurrentLogin } from '../../shared/github/gh-cli.ts';
import {
  createIssueComment,
  editIssueComment,
  getIssue,
  getIssueComments,
  removeLabel,
} from '../../shared/github/issues.ts';
import { logger } from '../../shared/logging/logger.ts';
import { hashIssueBody } from '../../shared/plan/body-sha.ts';
import { findPlanCommentIdToEdit, formatPlanMarker } from '../../shared/plan/plan-marker.ts';

type PublishArguments = { issueNumber: number; plannedBodySha: string };

function parsePublishArguments(args: string[]): PublishArguments {
  const [issueArgument, shaArgument] = args;
  const isValid = args.length === 2
    && /^[1-9][0-9]*$/.test(issueArgument)
    && /^[0-9a-f]{64}$/.test(shaArgument);
  if (!isValid) throw new Error('usage: publish-plan <issue-number> <body-sha> < plan');
  return { issueNumber: Number(issueArgument), plannedBodySha: shaArgument };
}

async function readPlanFromStdin(): Promise<string> {
  process.stdin.setEncoding('utf8');
  let planText = '';
  for await (const chunk of process.stdin) planText += chunk;
  if (planText.trim() === '') throw new Error('the plan on stdin is empty');
  return planText;
}

function formatPlanComment(plannedBodySha: string, planText: string): string {
  return `${formatPlanMarker(plannedBodySha)}\n\n${planText.replaceAll('\r\n', '\n').trim()}\n`;
}

async function main(): Promise<void> {
  const { issueNumber, plannedBodySha } = parsePublishArguments(process.argv.slice(2));
  const planText = await readPlanFromStdin();
  const [issue, comments, login] = await Promise.all([
    getIssue(issueNumber),
    getIssueComments(issueNumber),
    getCurrentLogin(),
  ]);

  // 1. Body unchanged.
  if (hashIssueBody(issue.body) !== plannedBodySha) {
    throw new Error(`the body of #${issueNumber} changed after print-body-sha: plan again`);
  }

  // 2. Trusted author.
  if (!config.trustedAuthors.includes(login)) {
    throw new Error(`${login} is not a trusted author: the orchestrator would ignore this plan`);
  }

  // 3. Approval removed, before the comment changes. A crash between the two leaves the old plan unapproved and
  // with the old body sha, so the next run plans again; the other order could leave the old approval on the new plan.
  // Not closed: an approval added after the labels were read and before the comment is written covers a plan
  // the approver has not seen. The window is a few requests long.
  const hadApproval = issue.labels.includes(config.labels.planApproved);
  if (hadApproval) await removeLabel(issueNumber, config.labels.planApproved);

  // 4. One plan comment, edited in place.
  const commentBody = formatPlanComment(plannedBodySha, planText);
  const existingCommentId = findPlanCommentIdToEdit(comments, login);
  if (existingCommentId === null) {
    await createIssueComment(issueNumber, commentBody);
  } else {
    await editIssueComment(existingCommentId, commentBody);
  }

  const commentResult = existingCommentId === null ? 'created' : `${existingCommentId} edited`;
  const approvalResult = hadApproval ? `, ${config.labels.planApproved} removed` : '';
  logger.info(`issue #${issueNumber}: plan comment ${commentResult}${approvalResult}`);
}

runCommand('publish-plan', main);
