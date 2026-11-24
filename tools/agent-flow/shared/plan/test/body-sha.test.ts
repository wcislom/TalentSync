import assert from 'node:assert/strict';
import { test } from 'node:test';
import { hashIssueBody } from '../body-sha.ts';

test('CRLF and LF bodies have the same hash', () => {
  assert.equal(hashIssueBody('line one\r\nline two\r\n'), hashIssueBody('line one\nline two\n'));
});

test('the hash is lowercase hex sha256 of the UTF-8 body', () => {
  assert.equal(hashIssueBody('abc'), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
});

test('a changed body has a different hash', () => {
  assert.notEqual(hashIssueBody('scope A'), hashIssueBody('scope B'));
});
