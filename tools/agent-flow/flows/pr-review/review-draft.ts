import type { NewReview, OpenThread, PostedReview, ReviewComment } from '../../shared/github/reviews.ts';
import type { CommentableLines } from './diff-lines.ts';
import { rulePattern } from './review-output.ts';
import type { Finding, ReviewOutput } from './review-output.ts';

export type ReviewDraft = NewReview & { findingsInBody: number; findingsRepeated: number };

export type ReviewDraftInput = {
  headSha: string;
  reviewOutput: ReviewOutput;
  commentableLines: CommentableLines;
  openThreads: OpenThread[];
  trustedAuthors: string[];
};

// Markers count only from trusted authors: anyone can comment on a PR, and a copied marker would otherwise
// silence a finding or the whole review.
function formatReviewMarker(headSha: string): string {
  return `<!-- agent:review head=${headSha} -->`;
}

function formatFindingMarker(rule: string): string {
  return `<!-- agent:finding rule=${rule} -->`;
}

const findingMarkerPattern = new RegExp(`<!-- agent:finding rule=(${rulePattern.source.slice(1, -1)}) -->`);

export function isAlreadyReviewed(reviews: PostedReview[], headSha: string, trustedAuthors: string[]): boolean {
  const marker = formatReviewMarker(headSha);
  return reviews.some((review) => trustedAuthors.includes(review.authorLogin) && review.body.includes(marker));
}

// The findings are the model's text about an untrusted PR: an @mention in them must not notify anyone.
function neutralizeMentions(text: string): string {
  return text.replaceAll(/@(?=[A-Za-z0-9])/g, '@​');
}

function findingKey(path: string, line: number, rule: string): string {
  return JSON.stringify([path, line, rule]);
}

function openFindingKeys(openThreads: OpenThread[], trustedAuthors: string[]): Set<string> {
  const keys = new Set<string>();
  for (const thread of openThreads) {
    const rule = findingMarkerPattern.exec(thread.body)?.[1];
    if (rule === undefined || thread.line === null || !trustedAuthors.includes(thread.authorLogin)) continue;
    keys.add(findingKey(thread.path, thread.line, rule));
  }
  return keys;
}

function formatFindingHeading(finding: Finding): string {
  return `**${finding.severity}** · ${finding.rule}`;
}

// A fence longer than any backtick run in the text, so the summary cannot close it.
function fence(text: string): string {
  const longestRun = Math.max(0, ...[...text.matchAll(/`+/g)].map((match) => match[0].length));
  const marks = '`'.repeat(Math.max(3, longestRun + 1));
  return `${marks}text\n${text}\n${marks}`;
}

export function buildReviewDraft(input: ReviewDraftInput): ReviewDraft {
  const { headSha, reviewOutput, commentableLines, openThreads, trustedAuthors } = input;
  const openKeys = openFindingKeys(openThreads, trustedAuthors);
  const comments: ReviewComment[] = [];
  const findingsInBody: Finding[] = [];
  let findingsRepeated = 0;

  for (const finding of reviewOutput.findings) {
    if (openKeys.has(findingKey(finding.path, finding.line, finding.rule))) {
      findingsRepeated += 1;
    } else if (commentableLines.get(finding.path)?.has(finding.line)) {
      comments.push({
        path: finding.path,
        line: finding.line,
        side: 'RIGHT',
        body: `${formatFindingHeading(finding)}\n\n${neutralizeMentions(finding.body)}\n\n`
          + formatFindingMarker(finding.rule),
      });
    } else {
      findingsInBody.push(finding);
    }
  }

  const bodyParts = [formatReviewMarker(headSha), `**Agent review** (review-pr) of ${headSha.slice(0, 7)}`,
    fence(reviewOutput.summary)];
  if (findingsInBody.length > 0) {
    const bullets = findingsInBody.map((finding) =>
      `- ${formatFindingHeading(finding)} · \`${finding.path}:${finding.line}\`: ${neutralizeMentions(finding.body)}`);
    bodyParts.push(`**Findings on lines outside the diff**\n\n${bullets.join('\n')}`);
  }
  if (findingsRepeated > 0) {
    bodyParts.push(`${findingsRepeated} finding(s) already have an open thread and are not repeated.`);
  }

  return {
    commitId: headSha,
    body: `${bodyParts.join('\n\n')}\n`,
    comments,
    findingsInBody: findingsInBody.length,
    findingsRepeated,
  };
}
