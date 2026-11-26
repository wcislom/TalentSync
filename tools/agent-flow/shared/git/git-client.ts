import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from '../config/config.ts';

const execFileAsync = promisify(execFile);

const remote = 'origin';

// `action` says what we tried to do. It is used in the error message: "could not <action>: <git's error>".
async function runGit(args: string[], action: string, extraEnvironment?: Record<string, string>): Promise<string> {
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

// New files that git does not track yet count too. Otherwise they would move with us to the agent's branch
// and end up in the agent's commit.
export async function hasUncommittedChanges(): Promise<boolean> {
  const stdout = await runGit(['status', '--porcelain'], 'read the working tree status');
  return stdout.trim() !== '';
}

export async function remoteBranchExists(branch: string): Promise<boolean> {
  const stdout = await runGit(['ls-remote', '--heads', remote, branch], `look up ${branch} on ${remote}`);
  return stdout.trim() !== '';
}

export async function fetchBranch(branch: string): Promise<void> {
  await runGit(['fetch', remote, branch], `fetch ${branch} from ${remote}`);
}

// -C overwrites a local branch with the same name. If an earlier run crashed, its local commits that were never
// pushed are dropped. That is on purpose: the flow keeps its state in GitHub, not in a local checkout, so we
// always start from what is on GitHub.
export async function switchToNewBranch(branch: string, baseBranch: string): Promise<void> {
  await runGit(['switch', '-C', branch, '--no-track', `${remote}/${baseBranch}`],
    `switch to ${branch} from ${remote}/${baseBranch}`);
}

export async function getCurrentBranch(): Promise<string> {
  const stdout = await runGit(['branch', '--show-current'], 'read the current branch');
  return stdout.trim();
}

export async function countCommitsAhead(baseBranch: string): Promise<number> {
  const stdout = await runGit(['rev-list', '--count', `${remote}/${baseBranch}..HEAD`],
    `count the commits ahead of ${remote}/${baseBranch}`);
  return Number(stdout.trim());
}

// The commit is authored by the agent's bot, not by whoever runs the flow. The e-mail is the bot's noreply
// address, so GitHub shows the bot as the author. Only this one git process gets the identity.
export async function commitAll(message: string): Promise<void> {
  const { login, userId } = config.agentBot;
  const email = `${userId}+${login}@users.noreply.github.com`;
  await runGit(['add', '--all'], 'stage the changes');
  await runGit(['commit', '--message', message], 'commit the changes', {
    GIT_AUTHOR_NAME: login,
    GIT_AUTHOR_EMAIL: email,
    GIT_COMMITTER_NAME: login,
    GIT_COMMITTER_EMAIL: email,
  });
}

// Pushes as the agent's GitHub App, with its token.
// - No force push. If another run pushed first, this push fails instead of overwriting that work.
// - The token is given only to this git process, through environment variables. It is not on the command line
//   (other programs can read command lines) and it is not saved in .git/config.
// - The first, empty extraheader value removes an Authorization header set earlier. actions/checkout sets one
//   in CI, and with two such headers the push fails.
// - The empty credential.helper turns off saved logins. If the token is rejected, the push must fail. It must
//   not quietly push with the user's own account.
export async function pushBranch(branch: string, token: string): Promise<void> {
  const authorization = Buffer.from(`x-access-token:${token}`).toString('base64');
  const extraHeaderKey = 'http.https://github.com/.extraheader';
  await runGit(['push', remote, `${branch}:refs/heads/${branch}`], `push ${branch} to ${remote}`, {
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
