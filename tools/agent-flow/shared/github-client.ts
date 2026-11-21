import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from './config.ts';

const execFileAsync = promisify(execFile);

export type IssueComment = { authorLogin: string; body: string };
export type TimelineEvent = { event: string; labelName: string | null; createdAt: string };
export type Issue = { body: string; labels: string[] };

async function gh(args: string[]): Promise<string> {
  try {
    const { stdout } = await execFileAsync('gh', args, {
      cwd: config.repoRoot,
      maxBuffer: 64 * 1024 * 1024,
      windowsHide: true,
    });
    return stdout;
  } catch (error) {
    const stderr = (error as { stderr?: string }).stderr?.trim() ?? '';
    throw new Error(`gh ${args.slice(0, 2).join(' ')} failed: ${stderr}`);
  }
}

async function ghJson<T>(args: string[]): Promise<T> {
  return JSON.parse(await gh(args)) as T;
}

// --slurp wraps every page in one outer array, so pagination never yields concatenated JSON documents.
async function ghApiAll<T>(endpoint: string): Promise<T[]> {
  const pages = await ghJson<T[][]>(['api', endpoint, '--paginate', '--slurp']);
  return pages.flat();
}

export async function getIssue(issueNumber: number): Promise<Issue> {
  const raw = await ghJson<{ body: string | null; labels: { name: string }[] }>(
    ['issue', 'view', String(issueNumber), '--json', 'body,labels']);
  return { body: raw.body ?? '', labels: raw.labels.map((label) => label.name) };
}

export async function getIssueComments(issueNumber: number): Promise<IssueComment[]> {
  const raw = await ghApiAll<{ user: { login: string } | null; body: string | null }>(
    `repos/{owner}/{repo}/issues/${issueNumber}/comments`);
  return raw.map((comment) => ({ authorLogin: comment.user?.login ?? '', body: comment.body ?? '' }));
}

export async function getIssueTimeline(issueNumber: number): Promise<TimelineEvent[]> {
  const raw = await ghApiAll<{ event: string; label?: { name: string }; created_at: string }>(
    `repos/{owner}/{repo}/issues/${issueNumber}/timeline`);
  return raw.map((event) => ({ event: event.event, labelName: event.label?.name ?? null, createdAt: event.created_at }));
}

export async function hasPullRequestFrom(branch: string): Promise<boolean> {
  const pullRequests = await ghJson<{ number: number }[]>(
    ['pr', 'list', '--head', branch, '--state', 'all', '--json', 'number']);
  return pullRequests.length > 0;
}

export async function addLabel(issueNumber: number, label: string): Promise<void> {
  await gh(['issue', 'edit', String(issueNumber), '--add-label', label]);
}

export async function removeLabel(issueNumber: number, label: string): Promise<void> {
  await gh(['issue', 'edit', String(issueNumber), '--remove-label', label]);
}
