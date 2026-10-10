# TalentSync domain

Normative. Code that contradicts the rules below is a bug (see "When docs and code disagree" in `CLAUDE.md`).
Acceptance scenarios reference the rule IDs (D1–D6).

## Business context

The business is a broker of independent IT consultants on B2B contracts. It recruits consultants into its network
and matches them with client projects. Candidate data captured in recruitment (Teamtailor) must reach the CRM
(relationships, sales) and the Matching Platform (where account managers match consultants to client needs).

TalentSync has no users of its own. It is a courier (deliver every change, no loss, no duplicates)
and a translator (recruitment language → matching language).

| Actor | Works in | Question they answer |
|---|---|---|
| Candidate | Teamtailor application form | Do I want to join the network? |
| Recruiter | Teamtailor | Should this candidate join the network? |
| Sales | CRM | What is our relationship with this consultant? |
| Account manager | Matching Platform | Which consultant fits this client need? |

## Glossary

| Term | Meaning | Context | Not the same as |
|---|---|---|---|
| Candidate | A person who applied through the ATS, identified by `candidateId` | Recruitment | Consultant |
| Job | An opening the business recruits for | Recruitment | Client project |
| Job application | A candidate's application to one job. One candidate has many | Recruitment | ConsultantProfile |
| Stage | Customer-defined step of a job application (locally `New`, `Screening`, `Interview`, `Qualified`, `Rejected`) | Recruitment, ATS term | ApplicationStatus |
| ApplicationStatus | Our interpretation of a stage, produced by the ACL | Recruitment, our model | Stage |
| Qualified | The stage named in `Teamtailor:QualifiedStageName`: recruitment accepted the person into the network | Recruitment | A match with a client |
| Contact | The candidate's record in the CRM, keyed by `candidateId` | CRM | ConsultantProfile |
| Consultant | A qualified candidate, as seen by matching | Consultant Matching | Candidate |
| ConsultantProfile | The consultant's record in the Matching Platform, keyed by `candidateId` | Consultant Matching | Job application |
| Enrichment | An LLM suggestion of skills and seniority with a confidence score. A suggestion, never a decision | Consultant Matching | A match |
| EnrichmentStatus | `Enriched`, or `NotEnriched` when enrichment failed or its output was invalid | Consultant Matching | |
| Seniority | `Junior`, `Mid`, `Senior`, `Lead` | Consultant Matching | |
| Account manager | Matches consultants to client needs in the Matching Platform | Consultant Matching | Recruiter |
| Client | A company with a project. Not modelled in TalentSync | | |

## Bounded contexts

| | Recruitment | Consultant Matching |
|---|---|---|
| Owner of data and rules | Teamtailor | Matching Platform, after qualification |
| Our model | `Candidate`, `JobApplication`, `ApplicationStatus`: a thin, read-only snapshot produced by the ACL | `ConsultantProfile`, `Skill`, `Seniority`, `EnrichmentStatus`: a model with behaviour |
| Project | `TalentSync.Recruitment.Domain` | `TalentSync.Matching.Domain` |
| Our relation | Downstream of a third-party SaaS → anti-corruption layer | We push profiles through its API |

The recruitment model looks anemic on purpose: its rules live in Teamtailor and we only read it.
The two domain projects don't reference each other; `Matching.Application` translates between them.

One candidate has many job applications. Sync is one-way: Teamtailor → TalentSync → CRM / Matching Platform.

## Domain rules

- **D1** Candidate data is synced to the CRM on create, update and delete. Delete = the CRM marks the contact as
  deleted; the record stays, because the CRM owns relationship data on it. A contact that does not exist counts as
  already deleted.
- **D2** When a job application reaches the stage configured as qualified, a `ConsultantProfile` is created or updated.
  The ACL maps the customer-defined stage name to `ApplicationStatus` (config `Teamtailor:QualifiedStageName`);
  the use case checks the status and calls `ConsultantProfile.CreateFromQualifiedCandidate`.
- **D3** One profile per candidate, keyed by Teamtailor `candidateId`. A second qualified application updates the same
  profile. Email and phone are not identity keys (they change, and they are PII).
- **D4** Moving an application back from qualified does not change the profile. The Matching Platform owns it.
- **D5** LLM enrichment suggests skills and seniority with a confidence score. It never decides a match.
  If enrichment fails, the profile is still created with status `NotEnriched`.
- **D6** MVP: `candidate.update` does not refresh an existing profile. Matching.Worker ignores `candidate.*` events.
