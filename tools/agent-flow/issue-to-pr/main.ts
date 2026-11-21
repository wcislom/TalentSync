import { config } from '../shared/config.ts';
import { getIssue, getIssueComments, getIssueTimeline, pullRequestExists } from '../shared/github-client.ts';
import { runSkill } from '../shared/run-skill.ts';
import type { Session } from '../shared/run-skill.ts';
import { bodySha } from './body-sha.ts';
import { decide } from './decide.ts';
import type { StepName } from './decide.ts';
import { acquireLock, lockState, releaseLock } from './lock.ts';
import { findPlanBodySha } from './plan-marker.ts';

// A step whose skill does not exist yet only logs what it would run. #44 and #42 flip their entry.
const steps: Record<StepName, { skill: string; implemented: boolean }> = {
  explain: { skill: 'explain-ticket', implemented: true },
  plan: { skill: 'plan-ticket', implemented: false },
  implement: { skill: 'implement-ticket', implemented: false },
};

function parseIssueNumber(args: string[]): number {
  if (args.length !== 1 || !/^[1-9][0-9]*$/.test(args[0])) {
    throw new Error('usage: issue-to-pr <issue-number>');
  }
  return Number(args[0]);
}

async function main(): Promise<void> {
  const issueNumber = parseIssueNumber(process.argv.slice(2));
  const [issue, comments, timeline, prExists] = await Promise.all([
    getIssue(issueNumber),
    getIssueComments(issueNumber),
    getIssueTimeline(issueNumber),
    pullRequestExists(`${config.agentBranchPrefix}${issueNumber}`),
  ]);

  const decision = decide({
    lock: lockState(issue.labels, timeline, config.labels.lock),
    bodySha: bodySha(issue.body),
    planBodySha: findPlanBodySha(comments, config.trustedAuthors),
    planApproved: issue.labels.includes(config.labels.planApproved),
    pullRequestExists: prExists,
  }, new Date(), config.lockStaleAfterMinutes);

  if (decision.kind === 'stop') {
    console.log(`issue #${issueNumber}: stop, ${decision.reason}`);
    return;
  }

  console.log(`issue #${issueNumber}: run ${decision.steps.join(', ')}`);
  await acquireLock(issueNumber, config.labels.lock, decision.staleLockPresent);
  let turns = 0;
  let costUsd = 0;
  // Steps of one run share a session, so each sees what the previous one produced.
  let session: Session | undefined;
  try {
    for (const name of decision.steps) {
      const { skill, implemented } = steps[name];
      if (!implemented) {
        console.log(`[${name}] would run /${skill} ${issueNumber}`);
        continue;
      }
      const remaining = { maxTurns: config.budget.maxTurns - turns, maxBudgetUsd: config.budget.maxBudgetUsd - costUsd };
      if (remaining.maxTurns <= 0 || remaining.maxBudgetUsd <= 0) throw new Error(`budget spent before step ${name}`);
      console.log(`[${name}] running /${skill} ${issueNumber}`);
      const run = await runSkill(skill, String(issueNumber), remaining, session);
      turns += run.turns;
      costUsd += run.costUsd;
      session = run.session;
    }
  } finally {
    console.log(`issue #${issueNumber}: ${turns} turns, $${costUsd.toFixed(4)} in total`);
    try {
      await releaseLock(issueNumber, config.labels.lock);
    } catch (error) {
      // Not rethrown, so it cannot hide the step's own error; the label goes stale after lockStaleAfterMinutes.
      console.error(`issue #${issueNumber}: failed to release the lock: ${(error as Error).message}`);
      process.exitCode = 1;
    }
  }
}

main().catch((error: unknown) => {
  console.error(`issue-to-pr failed: ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
});
