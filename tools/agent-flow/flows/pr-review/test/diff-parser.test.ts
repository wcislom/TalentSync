import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parseCommentableLines } from '../diff-parser.ts';

const diff = [
  'diff --git a/src/A.cs b/src/A.cs',
  'index 1111111..2222222 100644',
  '--- a/src/A.cs',
  '+++ b/src/A.cs',
  '@@ -10,4 +10,5 @@ class A',
  ' context 10',
  '-removed',
  '+added 11',
  '+++ added 12, text starting with ++',
  ' context 13',
  '\\ No newline at end of file',
  '@@ -40,2 +41,2 @@',
  ' context 41',
  '+added 42',
  'diff --git a/old.md b/old.md',
  'deleted file mode 100644',
  '--- a/old.md',
  '+++ /dev/null',
  '@@ -1,1 +0,0 @@',
  '-gone',
  'diff --git a/new.md b/new.md',
  'new file mode 100644',
  '--- /dev/null',
  '+++ b/new.md',
  '@@ -0,0 +1,2 @@',
  '+one',
  '+two',
  '',
].join('\n');

test('added and context lines of the new version are commentable, removed lines are not', () => {
  const lines = parseCommentableLines(diff);
  assert.deepEqual([...lines.get('src/A.cs')!], [10, 11, 12, 13, 41, 42]);
});

test('a new file has every line, a deleted file has no entry', () => {
  const lines = parseCommentableLines(diff);
  assert.deepEqual([...lines.get('new.md')!], [1, 2]);
  assert.equal(lines.has('old.md'), false);
});
