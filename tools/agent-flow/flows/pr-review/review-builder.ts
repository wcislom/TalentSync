import type { NewReview, OpenThread, ReviewComment } from '../../shared/github/reviews.ts';
import type { CommentableLines } from './diff-parser.ts';
import { createFindingKey, findOpenFindingKeys, formatFindingMarker, formatReviewMarker } from './review-markers.ts';
import type { Finding, SkillOutput } from './skill-output-parser.ts';

export type BuiltReview = NewReview & { findingsInBody: number; findingsRepeated: number };

export type ReviewInput = {
  headSha: string;
  skillOutput: SkillOutput;
  commentableLines: CommentableLines;
  openThreads: OpenThread[];
  trustedAuthors: string[];
};

// The model wrote this text after reading an untrusted PR. So we:
// - break @mentions, so nobody gets a notification;
// - escape "<!--", so the text cannot hold a fake marker that hides a later finding or review.
function sanitizeModelText(text: string): string {
  return text.replaceAll(/@(?=[A-Za-z0-9])/g, '@​').replaceAll('<!--', '&lt;!--');
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

// Turns the skill's findings into one review:
// - a finding on a line shown in the diff becomes an inline comment;
// - a finding on any other line goes into the review body, so it is not lost;
// - a finding that already has an open thread from an earlier review is skipped.
export function buildReview(input: ReviewInput): BuiltReview {
  const { headSha, skillOutput, commentableLines, openThreads, trustedAuthors } = input;
  const openKeys = findOpenFindingKeys(openThreads, trustedAuthors);
  const comments: ReviewComment[] = [];
  const findingsInBody: Finding[] = [];
  let findingsRepeated = 0;

  for (const finding of skillOutput.findings) {
    if (openKeys.has(createFindingKey(finding.path, finding.line, finding.rule))) {
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
    sanitizeModelText(formatSummary(skillOutput.summary))];
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
