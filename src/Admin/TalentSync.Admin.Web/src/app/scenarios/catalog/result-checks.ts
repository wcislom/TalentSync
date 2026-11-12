import { ResultCheck } from '../model/result-check';

export const crmContact: ResultCheck = {
  label: 'CRM contact',
  url: (simulators) => simulators.crmContactsUrl(),
  // A non-list body is shown as it came: a throw here would run inside computed() and break the whole card.
  select: (body, candidateId) =>
    Array.isArray(body)
      ? (body.find((contact) => contact?.candidateId === candidateId) ?? 'No contact yet')
      : body,
};

export const crmCalls: ResultCheck = {
  label: 'CRM calls',
  url: (simulators, candidateId) => simulators.crmCallsUrl(candidateId),
};

export const consultantProfile: ResultCheck = {
  label: 'Consultant profile',
  url: (simulators, candidateId) => simulators.profileUrl(candidateId),
};

export const atsCalls: ResultCheck = {
  label: 'ATS simulator calls',
  url: (simulators, candidateId) => simulators.atsCallsUrl(candidateId),
};
