import { createHash } from 'node:crypto';

// Bodies edited in the browser come back with CRLF, bodies set through the API often with LF.
export function hashIssueBody(body: string): string {
  return createHash('sha256').update(body.replaceAll('\r\n', '\n'), 'utf8').digest('hex');
}
