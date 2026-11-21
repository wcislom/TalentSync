import assert from 'node:assert/strict';
import { test } from 'node:test';
import { bodySha } from '../body-sha.ts';

test('CRLF and LF bodies have the same hash', () => {
  assert.equal(bodySha('line one\r\nline two\r\n'), bodySha('line one\nline two\n'));
});

test('the hash is lowercase hex sha256 of the UTF-8 body', () => {
  assert.equal(bodySha('abc'), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
});

test('a changed body has a different hash', () => {
  assert.notEqual(bodySha('scope A'), bodySha('scope B'));
});
