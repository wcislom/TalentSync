import { gh, ghApiAll, ghJson } from './gh-cli.ts';

export type PostedReview = { authorLogin: string; body: string };
// An unresolved review thread, by its first comment.
export type OpenThread = { path: string; line: number | null; authorLogin: string; body: string };
export type ReviewComment = { path: string; line: number; side: 'RIGHT'; body: string };
export type NewReview = { commitId: string; body: string; comments: ReviewComment[] };

export async function getPullRequestReviews(pullRequestNumber: number): Promise<PostedReview[]> {
  const raw = await ghApiAll<{ user: { login: string } | null; body: string | null }>(
    `repos/{owner}/{repo}/pulls/${pullRequestNumber}/reviews`, `read the reviews of PR #${pullRequestNumber}`);
  return raw.map((review) => ({ authorLogin: review.user?.login ?? '', body: review.body ?? '' }));
}

// REST has no "resolved" flag on review comments, hence GraphQL. A thread's first comment carries the finding.
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
