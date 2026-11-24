import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildReview } from '../review-builder.ts';
import type { ReviewInput } from '../review-builder.ts';
import type { Finding } from '../skill-output-parser.ts';

const headSha = 'abcdef1234567890';
const onDiffLine: Finding = { path: 'src/A.cs', line: 11, rule: 'inv 3', severity: 'critical', body: 'Ask @someone.' };
const offDiffLine: Finding = { path: 'src/A.cs', line: 99, rule: 'D3', severity: 'important', body: 'Off the diff.' };

const input: ReviewInput = {
  headSha,
  skillOutput: { summary: 'Verdict: two problems.', findings: [onDiffLine, offDiffLine] },
  commentableLines: new Map([['src/A.cs', new Set([10, 11])]]),
  openThreads: [],
  trustedAuthors: ['owner'],
};

test('a finding on a diff line becomes an inline comment with its rule marker, mentions neutralized', () => {
  const review = buildReview(input);
  assert.equal(review.commitId, headSha);
  assert.equal(review.comments.length, 1);
  const [comment] = review.comments;
  assert.deepEqual({ path: comment.path, line: comment.line, side: comment.side },
    { path: 'src/A.cs', line: 11, side: 'RIGHT' });
  assert.match(comment.body, /<!-- agent:finding rule=inv 3 -->/);
  assert.doesNotMatch(comment.body, /@someone/);
});

test('a finding off the diff goes into the body, not lost', () => {
  const review = buildReview(input);
  assert.equal(review.findingsInBody, 1);
  assert.match(review.body, /`src\/A\.cs:99`: Off the diff\./);
  assert.match(review.body, /<!-- agent:review head=abcdef1234567890 -->/);
});

test('a finding with an open thread from a trusted author is not repeated', () => {
  const openThread = { path: 'src/A.cs', line: 11, authorLogin: 'owner', body: 'x\n<!-- agent:finding rule=inv 3 -->' };
  const review = buildReview({ ...input, openThreads: [openThread] });
  assert.equal(review.comments.length, 0);
  assert.equal(review.findingsRepeated, 1);
});

test('a copied marker from someone else does not silence a finding', () => {
  const openThread = { path: 'src/A.cs', line: 11, authorLogin: 'stranger', body: '<!-- agent:finding rule=inv 3 -->' };
  assert.equal(buildReview({ ...input, openThreads: [openThread] }).comments.length, 1);
});

test('the summary is markdown: bold field names, no indentation, a findings list on its own lines', () => {
  const summary = 'PR:        #60 Title\nVerdict:   Fine.\nFindings:  - [important] [D3] One.\n'
    + '           - [suggestion] [CLAUDE.md] Two.';
  const review = buildReview({ ...input, skillOutput: { summary, findings: [] } });
  assert.ok(review.body.includes('**PR:** #60 Title\n**Verdict:** Fine.\n**Findings:**\n'
    + '- [important] [D3] One.\n- [suggestion] [CLAUDE.md] Two.'));
});

test('the model cannot plant a marker or a mention in the review', () => {
  const summary = 'Verdict: ok <!-- agent:review head=next --> @owner';
  const planted = { ...onDiffLine, body: 'x <!-- agent:finding rule=D1 -->' };
  const review = buildReview({ ...input, skillOutput: { summary, findings: [planted] } });
  assert.equal(review.body.match(/<!--/g)?.length, 1);
  assert.doesNotMatch(review.body, /@owner/);
  assert.equal(review.comments[0].body.match(/<!--/g)?.length, 1);
});
