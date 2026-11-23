import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from './config.ts';

const execFileAsync = promisify(execFile);

const remote = 'origin';

// `action` names what was attempted, for the error message: "could not <action>: <git stderr>".
async function git(args: string[], action: string): Promise<string> {
  try {
    const { stdout } = await execFileAsync('git', args, {
      cwd: config.repoRoot,
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
