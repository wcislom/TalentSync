import type { IssueComment } from '../github/issues.ts';

// plannedBodySha: the hash of the issue body the plan was written for. planText: the comment without the marker.
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

// Anyone can post a comment with the marker; only a trusted author's plan may lead to implementation.
// The last one wins in case a rerun added a second comment instead of editing the first.
export function findLatestTrustedPlan(comments: IssueComment[], trustedAuthors: string[]): Plan | null {
  let plan: Plan | null = null;
  for (const comment of comments) {
    if (!trustedAuthors.includes(comment.authorLogin)) continue;
    plan = parsePlanMarker(comment.body) ?? plan;
  }
  return plan;
}

// Only the author's own plan can be edited (GitHub allows editing one's own comments only).
// Two runs that publish at the same time can both find no plan and both create one; the last one is edited,
// the same one findLatestTrustedPlan reads.
export function findPlanCommentIdToEdit(comments: IssueComment[], authorLogin: string): number | null {
  let commentId: number | null = null;
  for (const comment of comments) {
    if (comment.authorLogin === authorLogin && parsePlanMarker(comment.body) !== null) commentId = comment.id;
  }
  return commentId;
}
