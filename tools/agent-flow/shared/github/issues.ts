import { runGh, runGhApiAll, runGhJson } from './gh-cli.ts';

// authorAssociation is the author's relation to the repo as GitHub reports it, e.g. OWNER, COLLABORATOR or NONE.
export type Issue = { body: string; labels: string[]; authorAssociation: string };
export type IssueComment = { id: number; authorLogin: string; body: string };
export type TimelineEvent = { event: string; labelName: string | null; createdAt: string };

// Reads the issue from the REST API, because `gh issue view --json` does not return author_association.
export async function getIssue(issueNumber: number): Promise<Issue> {
  const raw = await runGhJson<{ body: string | null; labels: { name: string }[]; author_association: string }>(
    ['api', `repos/{owner}/{repo}/issues/${issueNumber}`], `read issue #${issueNumber}`);
  return {
    body: raw.body ?? '',
    labels: raw.labels.map((label) => label.name),
    authorAssociation: raw.author_association,
  };
}

export async function getIssueComments(issueNumber: number): Promise<IssueComment[]> {
  const raw = await runGhApiAll<{ id: number; user: { login: string } | null; body: string | null }>(
    `repos/{owner}/{repo}/issues/${issueNumber}/comments`, `read the comments of #${issueNumber}`);
  return raw.map((comment) => ({ id: comment.id, authorLogin: comment.user?.login ?? '', body: comment.body ?? '' }));
}

// The comment text goes through stdin as JSON, not as a command argument: a long plan could be longer than the
// Windows command line limit.
// `token`: when set, the call runs as the agent's GitHub App instead of the logged-in user.
export async function createIssueComment(issueNumber: number, body: string, token?: string): Promise<void> {
  await runGh(['api', `repos/{owner}/{repo}/issues/${issueNumber}/comments`, '--method', 'POST', '--input', '-'],
    `comment on #${issueNumber}`, { stdin: JSON.stringify({ body }), token });
}

export async function editIssueComment(commentId: number, body: string, token?: string): Promise<void> {
  await runGh(['api', `repos/{owner}/{repo}/issues/comments/${commentId}`, '--method', 'PATCH', '--input', '-'],
    `edit comment ${commentId}`, { stdin: JSON.stringify({ body }), token });
}

export async function getIssueTimeline(issueNumber: number): Promise<TimelineEvent[]> {
  const raw = await runGhApiAll<{ event: string; label?: { name: string }; created_at: string }>(
    `repos/{owner}/{repo}/issues/${issueNumber}/timeline`, `read the timeline of #${issueNumber}`);
  return raw.map((event) => ({
    event: event.event,
    labelName: event.label?.name ?? null,
    createdAt: event.created_at,
  }));
}

export async function addLabel(issueNumber: number, label: string): Promise<void> {
  await runGh(['issue', 'edit', String(issueNumber), '--add-label', label],
    `add the label ${label} to #${issueNumber}`);
}

export async function removeLabel(issueNumber: number, label: string, token?: string): Promise<void> {
  await runGh(['issue', 'edit', String(issueNumber), '--remove-label', label],
    `remove the label ${label} from #${issueNumber}`, { token });
}
