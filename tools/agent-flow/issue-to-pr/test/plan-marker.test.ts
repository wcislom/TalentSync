import assert from 'node:assert/strict';
import { test } from 'node:test';
import { formatPlanMarker, latestTrustedPlan, parsePlanMarker, findPlanCommentIdToEdit } from '../plan-marker.ts';

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

test('the last plan comment of the author is the one to edit', () => {
  const comments = [
    { id: 1, authorLogin: 'owner', body: formatPlanMarker(sha) },
    { id: 2, authorLogin: 'owner', body: 'a discussion comment' },
    { id: 3, authorLogin: 'owner', body: formatPlanMarker(otherSha) },
    { id: 4, authorLogin: 'owner', body: 'a later discussion comment' },
  ];
  assert.equal(findPlanCommentIdToEdit(comments, 'owner'), 3);
});

test('a plan comment of another author is never edited', () => {
  const comments = [{ id: 1, authorLogin: 'someone-else', body: formatPlanMarker(sha) }];
  assert.equal(findPlanCommentIdToEdit(comments, 'owner'), null);
});

test('a comment quoting the marker is not a plan comment', () => {
  const comments = [{ id: 1, authorLogin: 'owner', body: `Quoting: ${formatPlanMarker(sha)}` }];
  assert.equal(findPlanCommentIdToEdit(comments, 'owner'), null);
});

test('without a plan comment a new one is needed', () => {
  assert.equal(findPlanCommentIdToEdit([], 'owner'), null);
});
