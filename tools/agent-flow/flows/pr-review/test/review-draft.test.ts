import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildReviewDraft, isAlreadyReviewed } from '../review-draft.ts';
import type { ReviewDraftInput } from '../review-draft.ts';
import type { Finding } from '../review-output.ts';

const headSha = 'abcdef1234567890';
const onDiffLine: Finding = { path: 'src/A.cs', line: 11, rule: 'inv 3', severity: 'critical', body: 'Ask @someone.' };
const offDiffLine: Finding = { path: 'src/A.cs', line: 99, rule: 'D3', severity: 'important', body: 'Off the diff.' };

const input: ReviewDraftInput = {
  headSha,
  reviewOutput: { summary: 'Verdict: two problems.', findings: [onDiffLine, offDiffLine] },
  commentableLines: new Map([['src/A.cs', new Set([10, 11])]]),
  openThreads: [],
  trustedAuthors: ['owner'],
};

test('a finding on a diff line becomes an inline comment with its rule marker, mentions neutralized', () => {
  const draft = buildReviewDraft(input);
  assert.equal(draft.commitId, headSha);
  assert.equal(draft.comments.length, 1);
  const [comment] = draft.comments;
  assert.deepEqual({ path: comment.path, line: comment.line, side: comment.side },
    { path: 'src/A.cs', line: 11, side: 'RIGHT' });
  assert.match(comment.body, /<!-- agent:finding rule=inv 3 -->/);
  assert.doesNotMatch(comment.body, /@someone/);
});

test('a finding off the diff goes into the body, not lost', () => {
  const draft = buildReviewDraft(input);
  assert.equal(draft.findingsInBody, 1);
  assert.match(draft.body, /`src\/A\.cs:99`: Off the diff\./);
  assert.match(draft.body, /<!-- agent:review head=abcdef1234567890 -->/);
});

test('a finding with an open thread from a trusted author is not repeated', () => {
  const openThread = { path: 'src/A.cs', line: 11, authorLogin: 'owner', body: 'x\n<!-- agent:finding rule=inv 3 -->' };
  const draft = buildReviewDraft({ ...input, openThreads: [openThread] });
  assert.equal(draft.comments.length, 0);
  assert.equal(draft.findingsRepeated, 1);
});

test('a copied marker from someone else does not silence a finding', () => {
  const openThread = { path: 'src/A.cs', line: 11, authorLogin: 'stranger', body: '<!-- agent:finding rule=inv 3 -->' };
  assert.equal(buildReviewDraft({ ...input, openThreads: [openThread] }).comments.length, 1);
});

test('the summary is markdown: bold field names, no indentation, a findings list on its own lines', () => {
  const summary = 'PR:        #60 Title\nVerdict:   Fine.\nFindings:  - [important] [D3] One.\n'
    + '           - [suggestion] [CLAUDE.md] Two.';
  const draft = buildReviewDraft({ ...input, reviewOutput: { summary, findings: [] } });
  assert.ok(draft.body.includes('**PR:** #60 Title\n**Verdict:** Fine.\n**Findings:**\n'
    + '- [important] [D3] One.\n- [suggestion] [CLAUDE.md] Two.'));
});

test('the model cannot plant a marker or a mention in the review', () => {
  const summary = 'Verdict: ok <!-- agent:review head=next --> @owner';
  const planted = { ...onDiffLine, body: 'x <!-- agent:finding rule=D1 -->' };
  const draft = buildReviewDraft({ ...input, reviewOutput: { summary, findings: [planted] } });
  assert.equal(draft.body.match(/<!--/g)?.length, 1);
  assert.doesNotMatch(draft.body, /@owner/);
  assert.equal(draft.comments[0].body.match(/<!--/g)?.length, 1);
});

test('a review counts as done only from a trusted author at the same head', () => {
  const marker = `<!-- agent:review head=${headSha} -->`;
  assert.equal(isAlreadyReviewed([{ authorLogin: 'owner', body: marker }], headSha, ['owner']), true);
  assert.equal(isAlreadyReviewed([{ authorLogin: 'stranger', body: marker }], headSha, ['owner']), false);
  assert.equal(isAlreadyReviewed([{ authorLogin: 'owner', body: marker }], 'other', ['owner']), false);
});
