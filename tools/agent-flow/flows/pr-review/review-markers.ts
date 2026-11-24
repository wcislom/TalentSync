import type { OpenThread, PostedReview } from '../../shared/github/reviews.ts';
import { rulePattern } from './skill-output-parser.ts';

// Hidden HTML comments (markers) in our reviews let a later run recognise its own work:
// - agent:review marks a commit that was already reviewed, so a rerun does not review it again;
// - agent:finding marks the rule of an inline comment, so a new push does not repeat the same finding.
// We only trust markers written by trusted authors. Anyone can comment on a PR, and a copied marker must not
// hide a finding or skip a review.

export function formatReviewMarker(headSha: string): string {
  return `<!-- agent:review head=${headSha} -->`;
}

export function formatFindingMarker(rule: string): string {
  return `<!-- agent:finding rule=${rule} -->`;
}

const findingMarkerPattern = new RegExp(`<!-- agent:finding rule=(${rulePattern.source.slice(1, -1)}) -->`);

export function isAlreadyReviewed(reviews: PostedReview[], headSha: string, trustedAuthors: string[]): boolean {
  const marker = formatReviewMarker(headSha);
  return reviews.some((review) => trustedAuthors.includes(review.authorLogin) && review.body.includes(marker));
}

// A finding is identified by its file, line and rule. Two findings with the same key are the same problem.
export function findingKey(path: string, line: number, rule: string): string {
  return JSON.stringify([path, line, rule]);
}

// The keys of findings that already have an open thread from one of our earlier reviews.
export function findOpenFindingKeys(openThreads: OpenThread[], trustedAuthors: string[]): Set<string> {
  const keys = new Set<string>();
  for (const thread of openThreads) {
    const rule = findingMarkerPattern.exec(thread.body)?.[1];
    if (rule === undefined || thread.line === null || !trustedAuthors.includes(thread.authorLogin)) continue;
    keys.add(findingKey(thread.path, thread.line, rule));
  }
  return keys;
}
