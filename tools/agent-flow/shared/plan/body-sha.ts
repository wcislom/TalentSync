import { createHash } from 'node:crypto';

// The hash tells whether the issue body changed after the plan was written. Line endings are made the same
// first: a body edited in the browser comes back with CRLF, one set through the API often has LF. Without this,
// the same text could give two different hashes.
export function hashIssueBody(body: string): string {
  return createHash('sha256').update(body.replaceAll('\r\n', '\n'), 'utf8').digest('hex');
}
