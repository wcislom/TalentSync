export type PullRequestText = { title: string; body: string };

// Splits the skill's text into the PR title (the first line) and the body (the rest).
// We add `Closes #<n>` here instead of asking the model to write it: this line makes GitHub close the issue when
// the PR is merged, so it must always be there.
export function parsePullRequestText(text: string, issueNumber: number): PullRequestText {
  const [firstLine, ...bodyLines] = text.replaceAll('\r\n', '\n').split('\n');
  const title = firstLine.trim();
  if (title === '') throw new Error('the PR title (the first line on stdin) is empty');
  const body = bodyLines.join('\n').trim();
  const closingLine = `Closes #${issueNumber}`;
  return { title, body: body === '' ? `${closingLine}\n` : `${body}\n\n${closingLine}\n` };
}
