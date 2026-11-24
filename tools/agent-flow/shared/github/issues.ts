import { gh, ghApiAll, ghJson } from './gh-cli.ts';

export type Issue = { body: string; labels: string[] };
export type IssueComment = { id: number; authorLogin: string; body: string };
export type TimelineEvent = { event: string; labelName: string | null; createdAt: string };

export async function getIssue(issueNumber: number): Promise<Issue> {
  const raw = await ghJson<{ body: string | null; labels: { name: string }[] }>(
    ['issue', 'view', String(issueNumber), '--json', 'body,labels'], `read issue #${issueNumber}`);
  return { body: raw.body ?? '', labels: raw.labels.map((label) => label.name) };
}

export async function getIssueComments(issueNumber: number): Promise<IssueComment[]> {
  const raw = await ghApiAll<{ id: number; user: { login: string } | null; body: string | null }>(
    `repos/{owner}/{repo}/issues/${issueNumber}/comments`, `read the comments of #${issueNumber}`);
  return raw.map((comment) => ({ id: comment.id, authorLogin: comment.user?.login ?? '', body: comment.body ?? '' }));
}

// The body goes through stdin as JSON: as an argument it could exceed the Windows command line limit.
export async function createIssueComment(issueNumber: number, body: string): Promise<void> {
  await gh(['api', `repos/{owner}/{repo}/issues/${issueNumber}/comments`, '--method', 'POST', '--input', '-'],
    `comment on #${issueNumber}`, { stdin: JSON.stringify({ body }) });
}

export async function editIssueComment(commentId: number, body: string): Promise<void> {
  await gh(['api', `repos/{owner}/{repo}/issues/comments/${commentId}`, '--method', 'PATCH', '--input', '-'],
    `edit comment ${commentId}`, { stdin: JSON.stringify({ body }) });
}

export async function getIssueTimeline(issueNumber: number): Promise<TimelineEvent[]> {
  const raw = await ghApiAll<{ event: string; label?: { name: string }; created_at: string }>(
    `repos/{owner}/{repo}/issues/${issueNumber}/timeline`, `read the timeline of #${issueNumber}`);
  return raw.map((event) => ({
    event: event.event,
    labelName: event.label?.name ?? null,
    createdAt: event.created_at,
  }));
}

export async function addLabel(issueNumber: number, label: string): Promise<void> {
  await gh(['issue', 'edit', String(issueNumber), '--add-label', label], `add the label ${label} to #${issueNumber}`);
}

export async function removeLabel(issueNumber: number, label: string): Promise<void> {
  await gh(['issue', 'edit', String(issueNumber), '--remove-label', label],
    `remove the label ${label} from #${issueNumber}`);
}
