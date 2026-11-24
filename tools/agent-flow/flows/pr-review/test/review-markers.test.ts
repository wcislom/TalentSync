import assert from 'node:assert/strict';
import { test } from 'node:test';
import { findOpenFindingKeys, createFindingKey, isAlreadyReviewed } from '../review-markers.ts';

const headSha = 'abcdef1234567890';

test('a review counts as done only from a trusted author at the same head', () => {
  const marker = `<!-- agent:review head=${headSha} -->`;
  assert.equal(isAlreadyReviewed([{ authorLogin: 'owner', body: marker }], headSha, ['owner']), true);
  assert.equal(isAlreadyReviewed([{ authorLogin: 'stranger', body: marker }], headSha, ['owner']), false);
  assert.equal(isAlreadyReviewed([{ authorLogin: 'owner', body: marker }], 'other', ['owner']), false);
});

test('only open threads from trusted authors with a finding marker and a line count', () => {
  const marker = '<!-- agent:finding rule=inv 3 -->';
  const keys = findOpenFindingKeys([
    { path: 'a.cs', line: 1, authorLogin: 'owner', body: `text
${marker}` },
    { path: 'b.cs', line: 2, authorLogin: 'stranger', body: marker },
    { path: 'c.cs', line: null, authorLogin: 'owner', body: marker },
    { path: 'd.cs', line: 4, authorLogin: 'owner', body: 'no marker' },
  ], ['owner']);
  assert.deepEqual([...keys], [createFindingKey('a.cs', 1, 'inv 3')]);
});
