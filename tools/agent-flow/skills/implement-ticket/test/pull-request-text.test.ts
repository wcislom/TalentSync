import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parsePullRequestText } from '../pull-request-text.ts';

test('the first line is the title and the rest the body, closing the issue', () => {
  const text = 'fix(matching): Masks dotted phone numbers\r\n\r\nWhy: the mask missed them.\r\n';
  assert.deepEqual(parsePullRequestText(text, 35), {
    title: 'fix(matching): Masks dotted phone numbers',
    body: 'Why: the mask missed them.\n\nCloses #35\n',
  });
});

test('a title without a body still closes the issue', () => {
  assert.deepEqual(parsePullRequestText('fix: Title\n', 35), { title: 'fix: Title', body: 'Closes #35\n' });
});

test('an empty first line is refused', () => {
  assert.throws(() => parsePullRequestText('\nbody', 35), /title .* is empty/);
});
