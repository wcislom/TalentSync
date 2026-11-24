export const Severity = { Critical: 'critical', Important: 'important', Suggestion: 'suggestion' } as const;
export type Severity = (typeof Severity)[keyof typeof Severity];

export type Finding = { path: string; line: number; rule: string; severity: Severity; body: string };
export type SkillOutput = { summary: string; findings: Finding[] };

// The skill's output format: a plain-text summary, then a ```json block with the findings.
const findingsBlockPattern = /```json[ \t]*\r?\n([\s\S]*?)\r?\n```/g;
// The rule is put into a hidden HTML comment, so it must not contain characters that could close it.
export const rulePattern = /^[A-Za-z0-9 ._-]{1,40}$/;
const pathPattern = /^[^`\r\n]+$/;
const severities: readonly string[] = Object.values(Severity);

// The model read an untrusted PR, so we never fix bad output: we reject it.
// Error messages show only the finding number and the field name, never the content,
// because CI logs are public.
export function parseSkillOutput(resultText: string): SkillOutput {
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
