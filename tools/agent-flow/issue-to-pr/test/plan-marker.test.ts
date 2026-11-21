import assert from 'node:assert/strict';
import { test } from 'node:test';
import { findPlanBodySha, formatPlanMarker, parsePlanMarker } from '../plan-marker.ts';

const sha = 'a'.repeat(64);
const otherSha = 'b'.repeat(64);

test('a formatted marker parses back to its hash', () => {
  assert.equal(parsePlanMarker(`${formatPlanMarker(sha)}\n\nPlan text`), sha);
});

test('a marker that does not start the comment is ignored', () => {
  assert.equal(parsePlanMarker(`Quoting: ${formatPlanMarker(sha)}`), null);
});

test('a marker with a malformed hash is ignored', () => {
  assert.equal(parsePlanMarker('<!-- agent:plan body-sha=xyz -->'), null);
});

test('plan comments from untrusted authors are ignored', () => {
  const comments = [{ authorLogin: 'someone-else', body: formatPlanMarker(sha) }];
  assert.equal(findPlanBodySha(comments, ['owner']), null);
});

test('the last plan comment from a trusted author wins', () => {
  const comments = [
    { authorLogin: 'owner', body: formatPlanMarker(sha) },
    { authorLogin: 'owner', body: 'a discussion comment' },
    { authorLogin: 'owner', body: formatPlanMarker(otherSha) },
    { authorLogin: 'someone-else', body: formatPlanMarker(sha) },
  ];
  assert.equal(findPlanBodySha(comments, ['owner']), otherSha);
});
