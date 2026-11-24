// Per file, the lines of the new version a review comment can be put on: added and context lines inside a hunk.
// A comment on any other line makes GitHub reject the whole review.
export type CommentableLines = Map<string, Set<number>>;

const hunkHeaderPattern = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@/;

export function parseCommentableLines(diff: string): CommentableLines {
  const commentableLines: CommentableLines = new Map();
  let lines: Set<number> | null = null;
  let inFileHeader = false;
  let newLine = 0;

  for (const diffLine of diff.replaceAll('\r\n', '\n').split('\n')) {
    if (diffLine.startsWith('diff --git ')) {
      inFileHeader = true;
      lines = null;
      continue;
    }
    // Only in the header: inside a hunk, an added line whose text starts with "++" also starts with "+++ ".
    if (inFileHeader) {
      if (diffLine.startsWith('+++ b/')) {
        lines = new Set();
        commentableLines.set(diffLine.slice('+++ b/'.length), lines);
      }
      if (!diffLine.startsWith('@@')) continue;
    }
    const hunkHeader = hunkHeaderPattern.exec(diffLine);
    if (hunkHeader !== null) {
      inFileHeader = false;
      newLine = Number(hunkHeader[1]);
      continue;
    }
    // A deleted file (+++ /dev/null) has no new version to comment on.
    if (lines === null) continue;
    if (diffLine.startsWith('+') || diffLine.startsWith(' ')) {
      lines.add(newLine);
      newLine += 1;
    }
  }
  return commentableLines;
}
