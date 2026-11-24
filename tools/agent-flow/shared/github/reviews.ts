import { gh, ghApiAll, ghJson } from './gh-cli.ts';

export type PostedReview = { authorLogin: string; body: string };
// An open (unresolved) review thread, described by its first comment.
export type OpenThread = { path: string; line: number | null; authorLogin: string; body: string };
export type ReviewComment = { path: string; line: number; side: 'RIGHT'; body: string };
export type NewReview = { commitId: string; body: string; comments: ReviewComment[] };

export async function getPullRequestReviews(pullRequestNumber: number): Promise<PostedReview[]> {
  const raw = await ghApiAll<{ user: { login: string } | null; body: string | null }>(
    `repos/{owner}/{repo}/pulls/${pullRequestNumber}/reviews`, `read the reviews of PR #${pullRequestNumber}`);
  return raw.map((review) => ({ authorLogin: review.user?.login ?? '', body: review.body ?? '' }));
}

// The REST API does not say if a thread is resolved, so we use GraphQL.
// The first comment of a thread holds the finding.
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

// One API call with event COMMENT: the review is published completely or not at all.
// It never approves and never requests changes.
export async function createReview(pullRequestNumber: number, review: NewReview): Promise<void> {
  const { commitId, body, comments } = review;
  await gh(['api', `repos/{owner}/{repo}/pulls/${pullRequestNumber}/reviews`, '--method', 'POST', '--input', '-'],
    `publish the review of PR #${pullRequestNumber}`,
    { stdin: JSON.stringify({ commit_id: commitId, event: 'COMMENT', body, comments }) });
}
