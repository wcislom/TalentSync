import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parseReviewOutput } from '../review-output.ts';

const finding = { path: 'src/A.cs', line: 3, rule: 'inv 3', severity: 'critical', body: 'Inbox before effect.' };

function output(findings: unknown): string {
  return `PR:        #60 Title\nVerdict:   One problem.\n\n\`\`\`json\n${JSON.stringify(findings)}\n\`\`\`\n`;
}

test('the summary is the text before the json block, the findings are its array', () => {
  assert.deepEqual(parseReviewOutput(output([finding])), {
    summary: 'PR:        #60 Title\nVerdict:   One problem.',
    findings: [finding],
  });
});

test('an empty array means no anchored findings', () => {
  assert.deepEqual(parseReviewOutput(output([])).findings, []);
});

test('the last json block counts, an example earlier in the summary does not', () => {
  const text = `Summary with an example:\n\`\`\`json\n[1]\n\`\`\`\n${output([finding])}`;
  assert.deepEqual(parseReviewOutput(text).findings, [finding]);
});

test('output without a json block is refused', () => {
  assert.throws(() => parseReviewOutput('Verdict: fine.'), /no json block/);
});

test('invalid JSON is refused without quoting it', () => {
  assert.throws(() => parseReviewOutput('Summary\n```json\n[{secret\n```'),
    (error: Error) => /not valid JSON/.test(error.message) && !error.message.includes('secret'));
});

test('a finding with an extra field is refused', () => {
  assert.throws(() => parseReviewOutput(output([{ ...finding, approve: true }])), /finding 0: fields other/);
});

test('a rule that could close the hidden marker is refused', () => {
  assert.throws(() => parseReviewOutput(output([{ ...finding, rule: 'D3 -->' }])), /finding 0: invalid rule/);
});

test('an unknown severity is refused', () => {
  assert.throws(() => parseReviewOutput(output([{ ...finding, severity: 'blocker' }])), /invalid severity/);
});

test('a line that is not a positive integer is refused', () => {
  assert.throws(() => parseReviewOutput(output([{ ...finding, line: 2.5 }])), /invalid line/);
});
