import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeWorkflowCommand, format } from '../logger.ts';

test('a newline in a message cannot start a second workflow command', () => {
  assert.equal(escapeWorkflowCommand('failed\n::add-mask::x'), 'failed%0A::add-mask::x');
});

test('percent and carriage return are escaped, percent first', () => {
  assert.equal(escapeWorkflowCommand('100%\r\n'), '100%25%0D%0A');
});

test('durations under a minute are in seconds, longer ones in minutes and seconds', () => {
  assert.equal(format.duration(54_400), '54 s');
  assert.equal(format.duration(125_000), '2 min 5 s');
});

test('times are hours and minutes in UTC', () => {
  assert.equal(format.utcTime(new Date('2026-11-21T14:26:59Z')), '14:26 UTC');
});
