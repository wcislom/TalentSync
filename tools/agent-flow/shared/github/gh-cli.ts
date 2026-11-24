import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from '../config/config.ts';

const execFileAsync = promisify(execFile);

// `token`, when given, replaces the gh login for this one call (GH_TOKEN), e.g. to act as the agent's GitHub App.
export type GhOptions = { stdin?: string; token?: string };

// `action` names what was attempted, for the error message: "could not <action>: <gh stderr>".
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
    // JSON.parse quotes a piece of its input in the message, which could be issue text in a public log.
    throw new Error(`could not ${action}: gh returned invalid JSON`);
  }
}

// --slurp wraps every page in one outer array, so pagination never yields concatenated JSON documents.
export async function ghApiAll<T>(endpoint: string, action: string): Promise<T[]> {
  const pages = await ghJson<T[][]>(['api', endpoint, '--paginate', '--slurp'], action);
  return pages.flat();
}

export async function getCurrentLogin(): Promise<string> {
  const stdout = await gh(['api', 'user', '--jq', '.login'], 'read the current gh login');
  return stdout.trim();
}
