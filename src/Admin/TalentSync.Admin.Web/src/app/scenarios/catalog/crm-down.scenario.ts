import { NewCandidate, SEEDED_JOB_ID } from '../data-access/simulators-client';
import { Scenario } from '../model/scenario';
import { crmCalls, crmContact } from './result-checks';

export const crmDownScenario: Scenario = {
  key: 'crm',
  title: '2. CRM down',
  description: 'The CRM answers 503 for this candidate for 30 seconds, then the candidate applies.',
  expectation:
    'Within the 30 s the CRM calls are 503 and there is no contact: Sync.Worker retries in its handler loop, ' +
    'without parking. After them, one 201 and the contact is Active. Matching does not depend on the CRM.',
  async run(simulators, candidateId, step) {
    await step('Break the CRM for 30 s (503)', () => simulators.breakCrm({ mode: '503', seconds: 30, candidateId }));
    await step('Apply (candidate.create)', () => simulators.apply(SEEDED_JOB_ID, candidateId, candidate));
  },
  checks: [crmCalls, crmContact],
};

const candidate: NewCandidate = {
  firstName: 'Bob',
  lastName: 'Outage',
  email: 'bob.outage@example.com',
  resumeText: 'Mid-level frontend developer, 4 years with TypeScript and React.',
};
