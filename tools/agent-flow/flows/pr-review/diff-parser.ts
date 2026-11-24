// GitHub lets you put a review comment only on a line that is shown in the PR's diff: a changed line, or one
// of the few unchanged lines around a change. If even one comment points to another line (e.g. line 300 when
// only lines 10-20 changed), GitHub rejects the whole review.
// This function reads the diff and lists, for each file, the line numbers (in the new version of the file)
// where a comment is allowed. Findings on other lines go into the review body instead.
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
    // The file name comes from the "+++ b/<path>" line in the file's header. We look for it only in the header,
    // because an added line of code (which starts with "+") can also start with "+++ ".
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
    // A deleted file has no new version, so there is nothing to comment on.
    if (lines === null) continue;
    if (diffLine.startsWith('+') || diffLine.startsWith(' ')) {
      lines.add(newLine);
      newLine += 1;
    }
  }
  return commentableLines;
}
