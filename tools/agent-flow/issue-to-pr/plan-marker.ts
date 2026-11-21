import type { IssueComment } from '../shared/github-client.ts';

// plannedBodySha: the hash of the issue body the plan was written for.
export type Plan = { plannedBodySha: string };

const markerPattern = /^<!-- agent:plan body-sha=([0-9a-f]{64}) -->/;

export function formatPlanMarker(plannedBodySha: string): string {
  return `<!-- agent:plan body-sha=${plannedBodySha} -->`;
}

export function parsePlanMarker(commentBody: string): Plan | null {
  const match = markerPattern.exec(commentBody);
  return match === null ? null : { plannedBodySha: match[1] };
}

// Anyone can post a comment with the marker; only a trusted author's plan may lead to implementation.
// The last one wins in case a rerun added a second comment instead of editing the first.
export function latestTrustedPlan(comments: IssueComment[], trustedAuthors: string[]): Plan | null {
  let plan: Plan | null = null;
  for (const comment of comments) {
    if (!trustedAuthors.includes(comment.authorLogin)) continue;
    plan = parsePlanMarker(comment.body) ?? plan;
  }
  return plan;
}
