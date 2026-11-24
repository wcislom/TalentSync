import { runGh, runGhJson } from './gh-cli.ts';

export type PullRequest = { state: string; isDraft: boolean; isCrossRepository: boolean; headSha: string };
export type NewPullRequest = { head: string; base: string; title: string; body: string };

// PRs in any state count, also closed and merged ones: this branch already has its PR.
export async function findPullRequestFrom(branch: string): Promise<number | null> {
  const pullRequests = await runGhJson<{ number: number }[]>(
    ['pr', 'list', '--head', branch, '--state', 'all', '--json', 'number'], `list the PRs from ${branch}`);
  return pullRequests[0]?.number ?? null;
}

export async function getPullRequest(pullRequestNumber: number): Promise<PullRequest> {
  const raw = await runGhJson<{ state: string; isDraft: boolean; isCrossRepository: boolean; headRefOid: string }>(
    ['pr', 'view', String(pullRequestNumber), '--json', 'state,isDraft,isCrossRepository,headRefOid'],
    `read PR #${pullRequestNumber}`);
  return { state: raw.state, isDraft: raw.isDraft, isCrossRepository: raw.isCrossRepository, headSha: raw.headRefOid };
}

export async function getPullRequestDiff(pullRequestNumber: number): Promise<string> {
  return runGh(['pr', 'diff', String(pullRequestNumber), '--color', 'never'],
    `read the diff of PR #${pullRequestNumber}`);
}

// Opens the PR as ready for review (not a draft). Returns the PR's URL.
export async function createPullRequest(pullRequest: NewPullRequest, token: string): Promise<string> {
  const { head, base, title, body } = pullRequest;
  const stdout = await runGh(['pr', 'create', '--head', head, '--base', base, '--title', title, '--body-file', '-'],
    `open a PR from ${head}`, { stdin: body, token });
  return stdout.trim();
}
