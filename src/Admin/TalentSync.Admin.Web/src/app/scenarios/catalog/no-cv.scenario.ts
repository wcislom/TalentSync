import { NewCandidate, SEEDED_JOB_ID } from '../data-access/simulators-client';
import { Scenario } from '../model/scenario';
import { consultantProfile } from './result-checks';

export const noCvScenario: Scenario = {
  key: 'nocv',
  title: '3. No CV',
  description: 'A candidate without a CV is qualified.',
  expectation:
    'The profile is still created, as NotEnriched, with no skills and no seniority. The model is never called (D5).',
  async run(simulators, candidateId, step) {
    const { jobApplicationId } = await step('Apply (the simulator requires a CV, so a placeholder)', () =>
      simulators.apply(SEEDED_JOB_ID, candidateId, candidate),
    );
    await step('Remove the CV (candidate.update, ignored by Matching, D6)', () =>
      simulators.updateCandidate(candidateId, { resumeText: '' }),
    );
    await step('Move the application to Qualified', () => simulators.changeStage(jobApplicationId, 'Qualified'));
  },
  checks: [consultantProfile],
};

const candidate: NewCandidate = {
  firstName: 'Cyd',
  lastName: 'Blank',
  email: 'cyd.blank@example.com',
  resumeText: 'Placeholder, removed in the next step.',
};
