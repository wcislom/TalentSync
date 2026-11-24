import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from '../config/config.ts';

const execFileAsync = promisify(execFile);

// `token`: when set, this one gh call uses that token (GH_TOKEN) instead of the logged-in user.
// We use it to act as the agent's GitHub App.
export type GhOptions = { stdin?: string; token?: string };

// `action` says what we tried to do. It is used in the error message: "could not <action>: <gh's error>".
export async function gh(args: string[], action: string, options: GhOptions = {}): Promise<string> {
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

export async function ghJson<T>(args: string[], action: string): Promise<T> {
  const stdout = await gh(args, action);
  try {
    return JSON.parse(stdout) as T;
  } catch {
    // We do not pass on JSON.parse's own message: it quotes part of the input, which could be issue text, and
    // logs can be public.
    throw new Error(`could not ${action}: gh returned invalid JSON`);
  }
}

// Reads every page of a list from the GitHub API. Without --slurp, gh prints each page as a separate JSON
// document, one after another, which is not valid JSON. With --slurp we get one array of pages.
export async function ghApiAll<T>(endpoint: string, action: string): Promise<T[]> {
  const pages = await ghJson<T[][]>(['api', endpoint, '--paginate', '--slurp'], action);
  return pages.flat();
}

export async function getCurrentLogin(): Promise<string> {
  const stdout = await gh(['api', 'user', '--jq', '.login'], 'read the current gh login');
  return stdout.trim();
}
