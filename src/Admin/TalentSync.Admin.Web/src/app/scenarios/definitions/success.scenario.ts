import { NewCandidate, SEEDED_JOB_ID } from '../data-access/simulators-client';
import { Scenario } from '../model/scenario';
import { consultantProfile, crmContact } from './checks';

export const successScenario: Scenario = {
  key: 'ok',
  title: '1. Success',
  description: 'A candidate applies with a CV, then the application moves to Qualified.',
  expectation:
    'The CRM contact is Active. After a few seconds the profile is Enriched, with skills and seniority from the CV.',
  async run(simulators, candidateId, step) {
    const { jobApplicationId } = await step('Apply with a CV (candidate.create, job_application.create)', () =>
      simulators.apply(SEEDED_JOB_ID, candidateId, candidate),
    );
    await step('Move the application to Qualified (job_application.update)', () =>
      simu## Why
## Why

Step 1 of ADR 012: the demo scenarios were driven by hand from `docs/e2e.http`. They are now buttons in an
Angular app, which also hosts the Parking lot page in the next steps.

## Where to look

- `src/Admin/TalentSync.Admin.Web/src/app/scenarios/`: the page. One file per scenario in `catalog/`, HTTP to the
  simulators in `data-access/simulators-client.ts`. A step fails when the simulator's webhook did not reach Ingress,
  not only on an HTTP error.
- `core/runtime-config/`: `config.json` is read and checked before bootstrap, so one image runs everywhere (ADR 012).
- `simulators/*/Program.cs`: CORS for the app's origin. `UseCors` comes first, so a preflight never shows in `/calls`.
- Scenario 4 (poison) is new. Until the Parking lot page exists, it only shows the 400s in the ATS simulator's calls.lators.changeStage(jobApplicationId, 'Qualified'),
    );
  },
  checks: [crmContact, consultantProfile],
};

// Fictional. The email and phone in the CV are there on purpose: the enrichment adapter must mask them before the
// LLM call.
const candidate: NewCandidate = {
  firstName: 'Adam',
  lastName: 'Testowy',
  email: 'adam.testowy@example.com',
  phone: '+48 500 200 300',
  resumeText: `ADAM TESTOWY
Senior Backend Engineer / Tech Lead
Warsaw, Poland | adam.testowy@example.com | +48 500 200 300

SUMMARY
Backend engineer with 10 years of experience in .NET, the last 4 as a tech lead. Focused on distributed systems, integrations and cloud architecture on Azure. Comfortable owning a system from design to production support.

EXPERIENCE
2021-now: Tech Lead, Acme Freight (logistics platform)
- Led a team of 6 developers building an event-driven order platform on Azure Event Hubs and Service Bus.
- Designed the transactional outbox and idempotent consumers; cut duplicate shipments to zero.
- Migrated 12 services from VMs to Azure Kubernetes Service with Helm and GitHub Actions.
- Introduced OpenTelemetry tracing and Grafana dashboards; on-call rotation owner.

2017-2021: Senior .NET Developer, Blue Harbor Fintech
- Built payment APIs in ASP.NET Core and C#, 2,000 requests per second at peak.
- Designed SQL Server schemas and tuned queries; introduced EF Core and Dapper.
- Added Polly-based resilience and circuit breakers to third-party integrations.
- Mentored 3 junior developers.

2015-2017: .NET Developer, Example Software House
- Web applications in ASP.NET MVC, Entity Framework and Angular.
- Wrote unit and integration tests with xUnit.

SKILLS
C#, .NET 8-10, ASP.NET Core, Entity Framework Core, SQL Server, PostgreSQL, Redis, Azure (Event Hubs, Service Bus, Functions, AKS, Key Vault), Docker, Kubernetes, Terraform, GitHub Actions, OpenTelemetry, Domain-Driven Design, Clean Architecture, REST, gRPC, some Python and TypeScript.

EDUCATION
MSc Computer Science, Example University of Technology, 2015.

CERTIFICATIONS
Azure Solutions Architect Expert (AZ-305), Azure Developer Associate (AZ-204).

LANGUAGES
Polish (native), English (C1), German (A2).`,
};
