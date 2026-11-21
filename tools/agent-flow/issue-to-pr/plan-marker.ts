import type { IssueComment } from '../shared/github-client.ts';

const markerPattern = /^<!-- agent:plan body-sha=([0-9a-f]{64}) -->/;

export function formatPlanMarker(bodySha: string): string {
  return `<!-- agent:plan body-sha=${bodySha} -->`;
}

export function parsePlanMarker(commentBody: string): string | null {
  return markerPattern.exec(commentBody)?.[1] ?? null;
}

// Anyone can post a comment with the marker; only a trusted author's plan may lead to implementation.
// The last one wins in case a rerun added a second comment instead of editing the first.
export function findPlanBodySha(comments: IssueComment[], trustedAuthors: string[]): string | null {
  let sha: string | null = null;
  for (const comment of comments) {
    if (!trustedAuthors.includes(comment.authorLogin)) continue;
    sha = parsePlanMarker(comment.body) ?? sha;
  }
  return sha;
}
