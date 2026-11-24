export const Severity = { Critical: 'critical', Important: 'important', Suggestion: 'suggestion' } as const;
export type Severity = (typeof Severity)[keyof typeof Severity];

export type Finding = { path: string; line: number; rule: string; severity: Severity; body: string };
export type ReviewOutput = { summary: string; findings: Finding[] };

// The skill's contract: a plain-text summary, then a fenced json block with the anchored findings.
const findingsBlockPattern = /```json[ \t]*\r?\n([\s\S]*?)\r?\n```/g;
// The rule goes into a hidden HTML comment on the review comment, so it must not be able to close it.
export const rulePattern = /^[A-Za-z0-9 ._-]{1,40}$/;
const pathPattern = /^[^`\r\n]+$/;
const severities: readonly string[] = Object.values(Severity);

// The output is the model's, and the model read an untrusted PR: anything off the contract is refused, not
// repaired. Errors name the finding's index and field, never its content, because they end up in public CI logs.
export function parseReviewOutput(resultText: string): ReviewOutput {
  const lastBlock = [...resultText.matchAll(findingsBlockPattern)].at(-1);
  if (lastBlock === undefined) throw new Error('the skill output has no json block of findings');
  const summary = resultText.slice(0, lastBlock.index).trim();
  if (summary === '') throw new Error('the skill output has no summary before the findings');

  let parsed: unknown;
  try {
    parsed = JSON.parse(lastBlock[1]);
  } catch {
    throw new Error('the findings block is not valid JSON');
  }
  if (!Array.isArray(parsed)) throw new Error('the findings block is not an array');
  return { summary, findings: parsed.map(parseFinding) };
}

function parseFinding(item: unknown, index: number): Finding {
  const invalid = (what: string) => new Error(`finding ${index}: ${what}`);
  if (typeof item !== 'object' || item === null || Array.isArray(item)) throw invalid('not an object');
  const { path, line, rule, severity, body, ...rest } = item as Record<string, unknown>;
  if (Object.keys(rest).length > 0) throw invalid('fields other than path, line, rule, severity, body');
  if (typeof path !== 'string' || !pathPattern.test(path)) throw invalid('invalid path');
  if (typeof line !== 'number' || !Number.isInteger(line) || line < 1) throw invalid('invalid line');
  if (typeof rule !== 'string' || !rulePattern.test(rule)) throw invalid('invalid rule');
  if (typeof severity !== 'string' || !severities.includes(severity)) throw invalid('invalid severity');
  if (typeof body !== 'string' || body.trim() === '') throw invalid('empty body');
  return { path, line, rule, severity: severity as Severity, body: body.trim() };
}
