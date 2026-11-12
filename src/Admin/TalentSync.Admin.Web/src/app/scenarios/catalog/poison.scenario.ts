import { NewCandidate, SEEDED_JOB_ID } from '../data-access/simulators-client';
import { Scenario } from '../model/scenario';
import { atsCalls } from './result-checks';

export const poisonScenario: Scenario = {
  key: 'poison',
  title: '4. Poison event',
  description:
    'The ATS simulator answers 400 to every API call about this candidate for 2 minutes, then the candidate applies.',
  expectation:
    'A 400 is a permanent failure: Sync.Worker parks candidate.create and Matching.Worker parks ' +
    'job_application.create, then both move on. The ATS simulator calls show the 400s. ' +
    'Only if a worker fetches within the 2 minutes: a worker behind on its partition would see the anomaly expired ' +
    'and process the event normally. Clearing the anomaly by hand has the same effect.',
  async run(simulators, candidateId, step) {
    // Long enough for a backlog or a slow LLM call ahead on the partition; it affects only this candidate.
    await step('Break the ATS simulator API for 2 min (400)', () =>
      simulators.breakAts({ mode: 'bad-request', seconds: 120, candidateId }),
    );
    await step('Apply (candidate.create, job_application.create)', () =>
      simulators.apply(SEEDED_JOB_ID, candidateId, candidate),
    );
  },
  checks: [atsCalls],
};

const candidate: NewCandidate = {
  firstName: 'Pat',
  lastName: 'Poison',
  email: 'pat.poison@example.com',
  resumeText: 'Junior QA engineer, 1 year of manual testing.',
};
