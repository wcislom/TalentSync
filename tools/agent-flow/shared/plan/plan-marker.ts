import type { IssueComment } from '../github/issues.ts';

// plannedBodySha: the hash of the issue body at the time the plan was written.
// planText: the plan itself, i.e. the comment without the hidden marker line.
export type Plan = { plannedBodySha: string; planText: string };

const markerPattern = /^<!-- agent:plan body-sha=([0-9a-f]{64}) -->/;

export function formatPlanMarker(plannedBodySha: string): string {
  return `<!-- agent:plan body-sha=${plannedBodySha} -->`;
}

export function parsePlanMarker(commentBody: string): Plan | null {
  const match = markerPattern.exec(commentBody);
  if (match === null) return null;
  return { plannedBodySha: match[1], planText: commentBody.slice(match[0].length).trim() };
}

// Anyone can post a comment with the plan marker, so we only accept plans from trusted authors. If there are
// several (a rerun can create a second one instead of editing the first), the newest one wins.
export function findLatestTrustedPlan(comments: IssueComment[], trustedAuthors: string[]): Plan | null {
  let plan: Plan | null = null;
  for (const comment of comments) {
    if (!trustedAuthors.includes(comment.authorLogin)) continue;
    plan = parsePlanMarker(comment.body) ?? plan;
  }
  return plan;
}

// Finds the plan comment to edit. It must be our own comment, because GitHub lets you edit only your own
// comments. If two runs published at the same time, there can be two plans. We edit the newest one, which is
// also the one findLatestTrustedPlan reads.
export function findPlanCommentIdToEdit(comments: IssueComment[], authorLogin: string): number | null {
  let commentId: number | null = null;
  for (const comment of comments) {
    if (comment.authorLogin === authorLogin && parsePlanMarker(comment.body) !== null) commentId = comment.id;
  }
  return commentId;
}
