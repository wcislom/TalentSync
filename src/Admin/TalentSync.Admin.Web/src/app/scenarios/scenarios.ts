import { Check, Scenario } from './scenario';
import { SAMPLE_CV } from './sample-cv';

const JOB_ID = 'job-1';

const crmContact: Check = {
  label: 'CRM contact',
  url: (simulators) => simulators.crmContactsUrl(),
  select: (body, candidateId) =>
    (body as { candidateId: string }[]).find((contact) => contact.candidateId === candidateId) ?? 'No contact yet',
};

const crmCalls: Check = {
  label: 'CRM calls',
  url: (simulators, candidateId) => simulators.crmCallsUrl(candidateId),
};

const profile: Check = {
  label: 'Consultant profile',
  url: (simulators, candidateId) => simulators.profileUrl(candidateId),
};

const atsCalls: Check = {
  label: 'ATS simulator calls',
  url: (simulators, candidateId) => simulators.atsCallsUrl(candidateId),
};

export const SCENARIOS: Scenario[] = [
  {
    key: 'ok',
    title: '1. Success',
    description: 'A candidate applies with a CV, then the application moves to Qualified.',
    expectation:
      'The CRM contact is Active. After a few seconds the profile is Enriched, with skills and seniority from the CV.',
    async run(simulators, candidateId, step) {
      const { jobApplicationId } = await step('Apply with a CV (candidate.create, job_application.create)', () =>
        simulators.apply(JOB_ID, candidateId, {
          firstName: 'Adam',
          lastName: 'Testowy',
          email: 'adam.testowy@example.com',
          phone: '+48 500 200 300',
          resumeText: SAMPLE_CV,
        }),
      );
      await step('Move the application to Qualified (job_application.update)', () =>
        simulators.changeStage(jobApplicationId, 'Qualified'),
      );
    },
    checks: [crmContact, profile],
  },
  {
    key: 'crm',
    title: '2. CRM down',
    description: 'The CRM answers 503 for this candidate for 30 seconds, then the candidate applies.',
    expectation:
      'Within the 30 s the CRM calls are 503 and there is no contact: Sync.Worker retries in its handler loop, ' +
      'without parking. After them, one 201 and the contact is Active. Matching does not depend on the CRM.',
    async run(simulators, candidateId, step) {
      await step('Break the CRM for 30 s (503)', () =>
        simulators.breakCrm({ mode: '503', seconds: 30, candidateId }),
      );
      await step('Apply (candidate.create)', () =>
        simulators.apply(JOB_ID, candidateId, {
          firstName: 'Bob',
          lastName: 'Outage',
          email: 'bob.outage@example.com',
          resumeText: 'Mid-level frontend developer, 4 years with TypeScript and React.',
        }),
      );
    },
    checks: [crmCalls, crmContact],
  },
  {
    key: 'nocv',
    title: '3. No CV',
    description: 'A candidate without a CV is qualified.',
    expectation: 'The profile is still created, as NotEnriched, with no skills and no seniority. The model is never called (D5).',
    async run(simulators, candidateId, step) {
      const { jobApplicationId } = await step('Apply (the simulator requires a CV, so a placeholder)', () =>
        simulators.apply(JOB_ID, candidateId, {
          firstName: 'Cyd',
          lastName: 'Blank',
          email: 'cyd.blank@example.com',
          resumeText: 'Placeholder, removed in the next step.',
        }),
      );
      await step('Remove the CV (candidate.update, ignored by Matching, D6)', () =>
        simulators.updateCandidate(candidateId, { resumeText: '' }),
      );
      await step('Move the application to Qualified', () => simulators.changeStage(jobApplicationId, 'Qualified'));
    },
    checks: [profile],
  },
  {
    key: 'poison',
    title: '4. Poison event',
    description: 'The ATS simulator answers 400 to every API call about this candidate for 30 seconds, then the candidate applies.',
    expectation:
      'A 400 is a permanent failure: Sync.Worker parks candidate.create and Matching.Worker parks ' +
      'job_application.create, then both move on. The ATS simulator calls show the 400s. ' +
      'The anomaly expires by itself: clearing it by hand before a worker fetches would let the event through.',
    async run(simulators, candidateId, step) {
      await step('Break the ATS simulator API for 30 s (400)', () =>
        simulators.breakAts({ mode: 'bad-request', seconds: 30, candidateId }),
      );
      await step('Apply (candidate.create, job_application.create)', () =>
        simulators.apply(JOB_ID, candidateId, {
          firstName: 'Pat',
          lastName: 'Poison',
          email: 'pat.poison@example.com',
          resumeText: 'Junior QA engineer, 1 year of manual testing.',
        }),
      );
    },
    checks: [atsCalls],
  },
];
