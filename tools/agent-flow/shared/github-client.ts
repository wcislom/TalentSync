import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from './config.ts';

const execFileAsync = promisify(execFile);

export type IssueComment = { id: number; authorLogin: string; body: string };
export type TimelineEvent = { event: string; labelName: string | null; createdAt: string };
export type Issue = { body: string; labels: string[] };
export type PostedReview = { authorLogin: string; body: string };
// An unresolved review thread, by its first comment.
export type OpenThread = { path: string; line: number | null; authorLogin: string; body: string };
export type ReviewComment = { path: string; line: number; side: 'RIGHT'; body: string };
export type NewReview = { commitId: string; body: string; comments: ReviewComment[] };

// `token`, when given, replaces the gh login for this one call (GH_TOKEN), e.g. to act as the agent's GitHub App.
type GhOptions = { stdin?: string; token?: string };

// `action` names what was attempted, for the error message: "could not <action>: <gh stderr>".
async function gh(args: string[], action: string, options: GhOptions = {}): Promise<string> {
  const { stdin, token } = options;
  try {
    const execution = execFileAsync('gh', args, {
      cwd: config.repoRoot,
      env: token === undefined ? undefined : { ...process.env, GH_TOKEN: token },
      maxBuffer: 64 * 1024 * 1024,
      windowsHide: true,
    });
    execution.child.stdin?.end(stdin);
    const { stdout } = await execution;
    return stdout;
  } catch (error) {
    const stderr = (error as { stderr?: string }).stderr?.trim() ?? '';
    throw new Error(`could not ${action}: ${stderr || 'gh failed without a message'}`);
  }
}

async function ghJson<T>(args: string[], action: string): Promise<T> {
  const stdout = await gh(args, action);
  try {
    return JSON.parse(stdout) as T;
  } catch {
    // JSON.parse quotes a piece of its input in the message, which could be issue text in a public log.
    throw new Error(`could not ${action}: gh returned invalid JSON`);
  }
}

// --slurp wraps every page in one outer array, so pagination never yields concatenated JSON documents.
async function ghApiAll<T>(endpoint: string, action: string): Promise<T[]> {
  const pages = await ghJson<T[][]>(['api', endpoint, '--paginate', '--slurp'], action);
  return pages.flat();
}

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

export async function getCurrentLogin(): Promise<string> {
  const stdout = await gh(['api', 'user', '--jq', '.login'], 'read the current gh login');
  return stdout.trim();
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

// Any state counts, closed and merged included: the branch already produced its PR.
export async function findPullRequestFrom(branch: string): Promise<number | null> {
  const pullRequests = await ghJson<{ number: number }[]>(
    ['pr', 'list', '--head', branch, '--state', 'all', '--json', 'number'], `list the PRs from ${branch}`);
  return pullRequests[0]?.number ?? null;
}

export async function addLabel(issueNumber: number, label: string): Promise<void> {
  await gh(['issue', 'edit', String(issueNumber), '--add-label', label], `add the label ${label} to #${issueNumber}`);
}

export async function removeLabel(issueNumber: number, label: string): Promise<void> {
  await gh(['issue', 'edit', String(issueNumber), '--remove-label', label],
    `remove the label ${label} from #${issueNumber}`);
}

export type NewPullRequest = { head: string; base: string; title: string; body: string };

// Ready for review, not a draft. Returns the PR's URL.
export async function createPullRequest(pullRequest: NewPullRequest, token: string): Promise<string> {
  const { head, base, title, body } = pullRequest;
  const stdout = await gh(['pr', 'create', '--head', head, '--base', base, '--title', title, '--body-file', '-'],
    `open a PR from ${head}`, { stdin: body, token });
  return stdout.trim();
}

export type PullRequest = { state: string; isDraft: boolean; isCrossRepository: boolean; headSha: string };

export async function getPullRequest(pullRequestNumber: number): Promise<PullRequest> {
  const raw = await ghJson<{ state: string; isDraft: boolean; isCrossRepository: boolean; headRefOid: string }>(
    ['pr', 'view', String(pullRequestNumber), '--json', 'state,isDraft,isCrossRepository,headRefOid'],
    `read PR #${pullRequestNumber}`);
  return { state: raw.state, isDraft: raw.isDraft, isCrossRepository: raw.isCrossRepository, headSha: raw.headRefOid };
}

export async function getPullRequestDiff(pullRequestNumber: number): Promise<string> {
  return gh(['pr', 'diff', String(pullRequestNumber), '--color', 'never'], `read the diff of PR #${pullRequestNumber}`);
}

export async function getPullRequestReviews(pullRequestNumber: number): Promise<PostedReview[]> {
  const raw = await ghApiAll<{ user: { login: string } | null; body: string | null }>(
    `repos/{owner}/{repo}/pulls/${pullRequestNumber}/reviews`, `read the reviews of PR #${pullRequestNumber}`);
  return raw.map((review) => ({ authorLogin: review.user?.login ?? '', body: review.body ?? '' }));
}

// Only REST has no "resolved" flag on review comments, hence GraphQL. A thread's first comment carries the finding.
const reviewThreadsQuery = `
query($owner: String!, $name: String!, $number: Int!, $endCursor: String) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      reviewThreads(first: 100, after: $endCursor) {
        pageInfo { hasNextPage endCursor }
        nodes { isResolved path line comments(first: 1) { nodes { author { login } body } } }
      }
    }
  }
}`;

type ReviewThreadsPage = {
  data: { repository: { pullRequest: { reviewThreads: { nodes: {
    isResolved: boolean;
    path: string;
    line: number | null;
    comments: { nodes: { author: { login: string } | null; body: string }[] };
  }[] } } } };
};

export async function getUnresolvedReviewThreads(pullRequestNumber: number): Promise<OpenThread[]> {
  const pages = await ghJson<ReviewThreadsPage[]>(['api', 'graphql', '--paginate', '--slurp',
    '-f', `query=${reviewThreadsQuery}`, '-F', 'owner={owner}', '-F', 'name={repo}',
    '-F', `number=${pullRequestNumber}`], `read the review threads of PR #${pullRequestNumber}`);
  return pages.flatMap((page) => page.data.repository.pullRequest.reviewThreads.nodes)
    .filter((thread) => !thread.isResolved)
    .map((thread) => {
      const firstComment = thread.comments.nodes[0];
      return {
        path: thread.path,
        line: thread.line,
        authorLogin: firstComment?.author?.login ?? '',
        body: firstComment?.body ?? '',
      };
    });
}

// One call with event COMMENT: the review is published whole or not at all, and never approves or requests changes.
export async function createReview(pullRequestNumber: number, review: NewReview): Promise<void> {
  const { commitId, body, comments } = review;
  await gh(['api', `repos/{owner}/{repo}/pulls/${pullRequestNumber}/reviews`, '--method', 'POST', '--input', '-'],
    `publish the review of PR #${pullRequestNumber}`,
    { stdin: JSON.stringify({ commit_id: commitId, event: 'COMMENT', body, comments }) });
}
