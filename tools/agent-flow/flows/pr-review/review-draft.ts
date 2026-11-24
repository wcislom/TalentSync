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

// Hidden HTML comments (markers) in our reviews let a later run recognise its own work:
// - agent:review marks a commit that was already reviewed, so a rerun does not review it again;
// - agent:finding marks the rule of an inline comment, so a new push does not repeat the same finding.
// We only trust markers written by trusted authors. Anyone can comment on a PR, and a copied marker must not
// hide a finding or skip a review.
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

// The model wrote this text after reading an untrusted PR. So we:
// - break @mentions, so nobody gets a notification;
// - escape "<!--", so the text cannot hold a fake marker that hides a later finding or review.
function sanitizeModelText(text: string): string {
  return text.replaceAll(/@(?=[A-Za-z0-9])/g, '@​').replaceAll('<!--', '&lt;!--');
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

const summaryFieldPattern = /^([A-Z][A-Za-z ]*):\s+(.*)$/;

// The skill writes lines like "Field:   value" and indents the lines that continue them.
// We remove the indentation, because GitHub shows text indented by 4 spaces as code.
// Field names become bold. GitHub keeps single line breaks in reviews.
function formatSummary(summary: string): string {
  return summary.split('\n').map((line) => {
    const trimmed = line.trim();
    const field = summaryFieldPattern.exec(trimmed);
    if (field === null) return trimmed;
    const [, name, value] = field;
    return value.startsWith('- ') ? `**${name}:**\n${value}` : `**${name}:** ${value}`;
  }).join('\n');
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
        body: `${formatFindingHeading(finding)}\n\n${sanitizeModelText(finding.body)}\n\n`
          + formatFindingMarker(finding.rule),
      });
    } else {
      findingsInBody.push(finding);
    }
  }

  const bodyParts = [formatReviewMarker(headSha), `**Agent review** (review-pr) of ${headSha.slice(0, 7)}`,
    sanitizeModelText(formatSummary(reviewOutput.summary))];
  if (findingsInBody.length > 0) {
    const bullets = findingsInBody.map((finding) =>
      `- ${formatFindingHeading(finding)} · \`${finding.path}:${finding.line}\`: ${sanitizeModelText(finding.body)}`);
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
