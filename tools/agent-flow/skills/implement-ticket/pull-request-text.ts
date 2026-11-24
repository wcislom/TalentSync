export type PullRequestText = { title: string; body: string };

// The skill writes the title on the first line and the body after it. `Closes #<n>` is added here, not left to
// the model, because it is what closes the issue on merge.
export function parsePullRequestText(text: string, issueNumber: number): PullRequestText {
  const [firstLine, ...bodyLines] = text.replaceAll('\r\n', '\n').split('\n');
  const title = firstLine.trim();
  if (title === '') throw new Error('the PR title (the first line on stdin) is empty');
  const body = bodyLines.join('\n').trim();
  const closingLine = `Closes #${issueNumber}`;
  return { title, body: body === '' ? `${closingLine}\n` : `${body}\n\n${closingLine}\n` };
}
