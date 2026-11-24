// npm --prefix tools/agent-flow run publish-plan <issue-number> <body-sha> < plan.md
//
// The last command the plan-ticket skill runs. It publishes the plan as the issue's plan comment. Each issue has
// one plan comment, edited in place. <body-sha> comes from print-body-sha, which the skill ran before it read
// the issue body.
// Steps:
//   1. Stop if the issue body changed since then: the plan would describe an older version of the issue.
//   2. Stop if the logged-in gh user is not a trusted author: the orchestrator would ignore the plan and write
//      a new one.
//   3. Remove the agent:plan-approved label. An approval must always belong to the plan the person actually read.
//   4. Edit the existing plan comment, or create one if there is none. Other comments are never changed.

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

  // 1. Is the body unchanged?
  if (hashIssueBody(issue.body) !== plannedBodySha) {
    throw new Error(`the body of #${issueNumber} changed after print-body-sha: plan again`);
  }

  // 2. Is the gh user a trusted author?
  if (!config.trustedAuthors.includes(login)) {
    throw new Error(`${login} is not a trusted author: the orchestrator would ignore this plan`);
  }

  // 3. Remove the approval before changing the comment.
  //    If we crash between the two, the old plan stays without approval, so the next run plans again. In the
  //    other order, a crash could leave the old approval on the new plan.
  //    Known gap: if someone adds the approval after we read the labels and before we write the comment, the
  //    approval covers a plan they have not seen. That window is only a few requests long.
  const hadApproval = issue.labels.includes(config.labels.planApproved);
  if (hadApproval) await removeLabel(issueNumber, config.labels.planApproved);

  // 4. Write the plan: edit the existing comment, or create the first one.
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
