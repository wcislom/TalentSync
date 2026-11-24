import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from '../config/config.ts';

const execFileAsync = promisify(execFile);

const remote = 'origin';

// `action` names what was attempted, for the error message: "could not <action>: <git stderr>".
async function git(args: string[], action: string, extraEnvironment?: Record<string, string>): Promise<string> {
  try {
    const { stdout } = await execFileAsync('git', args, {
      cwd: config.repoRoot,
      env: extraEnvironment === undefined ? undefined : { ...process.env, ...extraEnvironment },
      maxBuffer: 64 * 1024 * 1024,
      windowsHide: true,
    });
    return stdout;
  } catch (error) {
    const stderr = (error as { stderr?: string }).stderr?.trim() ?? '';
    throw new Error(`could not ${action}: ${stderr || 'git failed without a message'}`);
  }
}

// Untracked files count: a switch would carry them onto the agent's branch and into its commit.
export async function hasUncommittedChanges(): Promise<boolean> {
  const stdout = await git(['status', '--porcelain'], 'read the working tree status');
  return stdout.trim() !== '';
}

export async function remoteBranchExists(branch: string): Promise<boolean> {
  const stdout = await git(['ls-remote', '--heads', remote, branch], `look up ${branch} on ${remote}`);
  return stdout.trim() !== '';
}

export async function fetchBranch(branch: string): Promise<void> {
  await git(['fetch', remote, branch], `fetch ${branch} from ${remote}`);
}

// -C resets a local branch of that name: a crashed run's commits that never reached the remote are dropped,
// because the flow's state lives in GitHub, not in a checkout.
export async function switchToNewBranch(branch: string, baseBranch: string): Promise<void> {
  await git(['switch', '-C', branch, '--no-track', `${remote}/${baseBranch}`],
    `switch to ${branch} from ${remote}/${baseBranch}`);
}

export async function getCurrentBranch(): Promise<string> {
  const stdout = await git(['branch', '--show-current'], 'read the current branch');
  return stdout.trim();
}

export async function countCommitsAhead(baseBranch: string): Promise<number> {
  const stdout = await git(['rev-list', '--count', `${remote}/${baseBranch}..HEAD`],
    `count the commits ahead of ${remote}/${baseBranch}`);
  return Number(stdout.trim());
}

export async function commitAll(message: string): Promise<void> {
  await git(['add', '--all'], 'stage the changes');
  await git(['commit', '--message', message], 'commit the changes');
}

// Never forced: a parallel run that pushed first makes this push fail instead of being overwritten.
// The token reaches this git process only, through its environment: not on the command line (other processes
// can read it there) and not in .git/config. The empty values clear an extraheader set before (actions/checkout
// sets one: two Authorization headers fail the push) and the credential helpers (a rejected token must fail,
// not fall back to the user's own credentials).
export async function pushBranch(branch: string, token: string): Promise<void> {
  const authorization = Buffer.from(`x-access-token:${token}`).toString('base64');
  const extraHeaderKey = 'http.https://github.com/.extraheader';
  await git(['push', remote, `${branch}:refs/heads/${branch}`], `push ${branch} to ${remote}`, {
    GIT_TERMINAL_PROMPT: '0',
    GIT_CONFIG_COUNT: '3',
    GIT_CONFIG_KEY_0: extraHeaderKey,
    GIT_CONFIG_VALUE_0: '',
    GIT_CONFIG_KEY_1: extraHeaderKey,
    GIT_CONFIG_VALUE_1: `AUTHORIZATION: basic ${authorization}`,
    GIT_CONFIG_KEY_2: 'credential.helper',
    GIT_CONFIG_VALUE_2: '',
  });
}
