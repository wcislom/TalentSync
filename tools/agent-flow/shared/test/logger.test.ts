import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeWorkflowCommand } from '../logger.ts';

test('a newline in a message cannot start a second workflow command', () => {
  assert.equal(escapeWorkflowCommand('failed\n::add-mask::x'), 'failed%0A::add-mask::x');
});

test('percent and carriage return are escaped, percent first', () => {
  assert.equal(escapeWorkflowCommand('100%\r\n'), '100%25%0D%0A');
});
