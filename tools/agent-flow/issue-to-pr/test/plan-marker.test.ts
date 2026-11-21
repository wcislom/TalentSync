import assert from 'node:assert/strict';
import { test } from 'node:test';
import { formatPlanMarker, latestTrustedPlan, parsePlanMarker } from '../plan-marker.ts';

const sha = 'a'.repeat(64);
const otherSha = 'b'.repeat(64);

test('a formatted marker parses back to its hash', () => {
  assert.deepEqual(parsePlanMarker(`${formatPlanMarker(sha)}\n\nPlan text`), { plannedBodySha: sha });
});

test('a marker that does not start the comment is ignored', () => {
  assert.equal(parsePlanMarker(`Quoting: ${formatPlanMarker(sha)}`), null);
});

test('a marker with a malformed hash is ignored', () => {
  assert.equal(parsePlanMarker('<!-- agent:plan body-sha=xyz -->'), null);
});

test('plan comments from untrusted authors are ignored', () => {
  const comments = [{ authorLogin: 'someone-else', body: formatPlanMarker(sha) }];
  assert.equal(latestTrustedPlan(comments, ['owner']), null);
});

test('the last plan comment from a trusted author wins', () => {
  const comments = [
    { authorLogin: 'owner', body: formatPlanMarker(sha) },
    { authorLogin: 'owner', body: 'a discussion comment' },
    { authorLogin: 'owner', body: formatPlanMarker(otherSha) },
    { authorLogin: 'someone-else', body: formatPlanMarker(sha) },
  ];
  assert.deepEqual(latestTrustedPlan(comments, ['owner']), { plannedBodySha: otherSha });
});
