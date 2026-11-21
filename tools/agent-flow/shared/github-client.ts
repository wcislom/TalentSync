import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from './config.ts';

const execFileAsync = promisify(execFile);

export type IssueComment = { authorLogin: string; body: string };
export type TimelineEvent = { event: string; labelName: string | null; createdAt: string };
export type Issue = { body: string; labels: string[] };

// `action` names what was attempted, for the error message: "could not <action>: <gh stderr>".
async function gh(args: string[], action: string): Promise<string> {
  try {
    const { stdout } = await execFileAsync('gh', args, {
      cwd: config.repoRoot,
      maxBuffer: 64 * 1024 * 1024,
      windowsHide: true,
    });
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
  const raw = await ghApiAll<{ user: { login: string } | null; body: string | null }>(
    `repos/{owner}/{repo}/issues/${issueNumber}/comments`, `read the comments of #${issueNumber}`);
  return raw.map((comment) => ({ authorLogin: comment.user?.login ?? '', body: comment.body ?? '' }));
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
