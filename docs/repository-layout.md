# TalentSync repository layout

Descriptive: once code exists, the code is current. Keep this file true when a project, folder or tool is added,
moved or removed. Dependency rules between these projects: `docs/architecture.md` (normative).

```
README.md                                   for humans: overview, running locally, simplifications
TalentSync.slnx                             solution, folders mirror the directories below
global.json                                 SDK pin, test runner = Microsoft.Testing.Platform
dotnet-tools.json                           local tool manifest: dotnet-ef (migrations, used by deploy/local/run.sh)
Directory.Build.props                       net10.0, nullable, implicit usings, warnings as errors
Directory.Packages.props                    central package versions (CPM)
src/
  Shared/
    TalentSync.Contracts                    event envelope, event names, SchemaVersion. References nothing
    TalentSync.Recruitment.Domain           our read-only model of recruitment: Candidate, JobApplication,
                                            ApplicationStatus, FetchResult<T> (Found | Deleted | Rejected),
                                            port IRecruitmentSource. References nothing
    TalentSync.Infrastructure.Teamtailor    ACL: TeamtailorRecruitmentSource (port adapter) over TeamtailorApiClient
                                            (HTTP, status mapping) and TeamtailorMapper, internal JSON:API DTOs, 429 handling,
                                            stage mapping from config, 404 → Deleted, 4xx / unmappable → Rejected,
                                            webhook parsing, signature v2 endpoint filter,
                                            public AddTeamtailorWebhooks() (Ingress)
                                            and AddTeamtailorApi() (workers). → Recruitment.Domain
    TalentSync.Infrastructure.Observability AddObservability(): OpenTelemetry traces, metrics and logs over OTLP,
                                            subscribes to every TalentSync.* ActivitySource and Meter, HTTP and SQL
                                            instrumentation, SpanExportPolicy (ADR 010).
                                            Only hosts reference it. References nothing in src/
    TalentSync.Infrastructure.Messaging     EventProcessorClient hosting, consumer handler skeleton (retry loop,
                                            failure classification), HandlerResult, Inbox and ParkingLot entities
                                            with schema-parameterised EF config. → Contracts
  Ingress/
    TalentSync.Ingress.Api                  host: webhook endpoint, envelope, IngressDbContext + Outbox, relay and
                                            EventHubProducerClient (only when OutboxRelay:Enabled), /health/live.
                                            → Contracts, Infrastructure.Teamtailor
  Sync/
    TalentSync.Sync.Application             UseCases/SyncCandidateToCrmUseCase, Ports/ICrm. → Recruitment.Domain
    TalentSync.Sync.Infrastructure          CrmClient + resilience, SyncDbContext. → Sync.Application, Infrastructure.Messaging
    TalentSync.Sync.Worker                  host: candidate.* handler (use case result → HandlerResult),
                                            composition root, group "sync-worker". Web host: only /health/live
                                            (liveness for an orchestrator, no dependency checks)
  Matching/
    TalentSync.Matching.Domain              ConsultantProfile, CandidateId, Skill, Confidence, Seniority,
                                            Enrichment (+ TryCreate → EnrichmentValidation), EnrichmentStatus. References nothing
    TalentSync.Matching.Application         UseCases/CreateConsultantProfileUseCase, Ports/IMatchingPlatform,
                                            IEnrichmentService, IEnrichmentResultStore, CvHash.
                                            → Matching.Domain, Recruitment.Domain
    TalentSync.Matching.Infrastructure      Llm/ (CvMasker, EnrichmentPrompt, ChatEnrichmentService over IChatClient,
                                            Fake/FakeChatClient),
                                            EnrichmentResults/, MatchingPlatformClient, MatchingDbContext with
                                            EnrichmentResultStore. → Matching.Application, Infrastructure.Messaging
    TalentSync.Matching.Worker              host: job_application.* handler (use case result → HandlerResult),
                                            composition root, group "matching-worker". Web host: only /health/live
                                            (liveness for an orchestrator, no dependency checks)
  Admin/
    TalentSync.Admin.Web                    Angular app (ADR 012), npm package talentsync-admin-web, Node pinned in .nvmrc.
                                            References nothing in src/ (HTTP only). Under src/app, by feature, one folder
                                            per component:
                                            core/runtime-config: RuntimeConfig + loader of public/config.json
                                            (simulator and traces addresses, read and checked before bootstrap; not
                                            app.config.ts, which holds Angular's providers)
                                            scenarios/: scenarios-page/, scenario-card/, data-access/simulators-client.ts
                                            (HTTP to the simulators), model/ (scenario.ts: Scenario + Step;
                                            result-check.ts), catalog/ (one *.scenario.ts per scenario with its
                                            candidate, result-checks.ts, scenario-catalog.ts with the page order)
simulators/                                 fake external systems, NOT part of the product (see simulators/CLAUDE.md).
  FakeTeamtailor                            No references to src/. CORS for the Admin app origin (Cors:AllowedOrigins).
  FakeCrm
  FakeMatchingPlatform
tests/
  TalentSync.Matching.Domain.Tests
  TalentSync.Sync.Application.Tests             use case: fetch result and CRM result → use case result
  TalentSync.Matching.Application.Tests         use case: qualified / not qualified / deleted / rejected, stored or new
                                                enrichment (stored before the upsert), failure → NotEnriched
  TalentSync.Matching.Infrastructure.Tests      Matching Platform client: request body, status mapping, resilience
  TalentSync.Infrastructure.Teamtailor.Tests    webhook parsing, stage mapping, 404 → Deleted, 4xx → Rejected
  TalentSync.Infrastructure.Messaging.Tests     consumer skeleton: inbox / parking lot mapping, failure classification, handler loop
  TalentSync.Infrastructure.Observability.Tests request span is a root with the SDK enabled, SpanExportPolicy
  TalentSync.Sync.Infrastructure.Tests          CRM client: status mapping, 404 on delete = done, resilience
  TalentSync.Matching.Evaluation.Tests          prompt evaluation: metrics, golden set hash, case files, report
  TalentSync.IntegrationTests                   acceptance scenarios (see .claude/rules/testing.md)
eval/                                       offline evaluation of the enrichment prompt (ADR 009), never run in CI
  README.md                                 what each metric catches, how to read a report
  TalentSync.Matching.Evaluation            console runner over ChatEnrichmentService. → Matching.Infrastructure
  cases/                                    golden set: one JSON per fictional CV
  reports/                                  committed reports; local/ is gitignored
  run-eval.sh                               runs the evaluation locally; provider, runs, output and model as variables at the top
deploy/
  infra/                                    what runs next to the hosts, shared by local/ and kubernetes/:
                                            docker-compose.yml (SQL Server, Event Hubs emulator, Azurite, simulators,
                                            Aspire Dashboard), eventhubs/Config.json, .env.example (.env is gitignored)
  local/                                    run.sh: the hosts with `dotnet run` and the Admin app with `npm start`
                                            (containers, build, npm ci when the lock file changed, migrations, four tabs)
  kubernetes/                               the hosts in a cluster (ADR 011): kind/cluster.yaml (pinned node image,
                                            host port 5065 → NodePort 30065 of Ingress.Api),
                                            helm/talentsync (one chart for all hosts; values-local.yaml for kind;
                                            templates/ingress-api.yaml, ingress-relay.yaml, sync-worker.yaml,
                                            matching-worker.yaml, migrations.yaml (pre-install / pre-upgrade hook Job)),
                                            create-secret.sh (the Secret `talentsync` from infra/.env and the local
                                            keys; restarts the hosts when a value changes), deploy.sh (Secret, host and
                                            migrations images tagged by commit, kind load, helm upgrade)
  images/                                   how images are built: host.Dockerfile (one image per host),
                                            simulator.Dockerfile, migrations.Dockerfile + migrations-entrypoint.sh
                                            (idempotent EF scripts applied with sqlcmd by the Helm hook Job)
tools/
  agent-flow/                               automations that run Claude Code skills through the Agent SDK (epic #48).
                                            One package.json, TypeScript run natively by Node (no build step, no tsc),
                                            tests with node:test (`npm --prefix tools/agent-flow test`), Node version in
                                            .nvmrc.
                                            Not part of the product, references nothing in src/
    shared/                                 code the flows and skills' tools share, by concern; each folder keeps its
                                            tests in test/:
      github/                               gh-cli.ts (every gh call: argument arrays, errors, JSON, pagination; a
                                            call can run as the agent's GitHub App), issues.ts (issue, comments,
                                            timeline, labels), pull-requests.ts (find, read, diff, open),
                                            reviews.ts (reviews, unresolved threads through GraphQL, publish one
                                            COMMENT review), app-token.ts (installation token of the agent's GitHub
                                            App from AGENT_APP_ID, AGENT_APP_INSTALLATION_ID,
                                            AGENT_APP_PRIVATE_KEY_PATH)
      git/                                  git-client.ts (every git call, argument arrays; the push takes the App's
                                            token through the process environment, never forced)
      runner/                               skill-runner.ts (a skill in a new or resumed SDK session, project
                                            settings only, turn and cost budget, its final text; formatSkillRun: the
                                            run's one-line summary), run-budget.ts (RunBudget: turns and cost spent
                                            against the run's limit; BudgetSpentError: a run that reached it)
      config/                               config.ts (repo root, main branch, labels, stale lock minutes, trusted
                                            authors and author associations, budget, agent branch)
      logging/                              logger.ts (the only place that writes log lines; groups, ::warning::
                                            and ::error:: in GitHub Actions; formatting of cost, duration and time)
      command/                              command-runner.ts (runCommand: a command's error → one log line and exit
                                            code 1; failProcess), issue-number.ts (the argument of single-issue and
                                            single-PR commands)
      plan/                                 body-sha.ts and plan-marker.ts (the plan comment's format: written by
                                            skills/plan-ticket, read by the orchestrator and skills/implement-ticket)
    flows/issue-to-pr/                      orchestrator: main.ts (read GitHub state → next step → lock → steps →
                                            release), next-step.ts (pure decision table), steps.ts (step → skill,
                                            stub or not), flow-log.ts (the run's log lines: ids, names and numbers
                                            only, CI logs are public),
                                            lock.ts (label agent:running, age from the issue timeline), test/
    flows/pr-review/                        review of one PR: main.ts (skips a closed, draft or fork PR and a commit
                                            already reviewed → /review-pr → publishes one COMMENT review with the
                                            gh login; the skill only reads, the flow publishes),
                                            skill-output-parser.ts (the skill's summary and JSON findings, rejected
                                            when off the format), diff-parser.ts (the lines of each file a review
                                            comment may go on), review-markers.ts (hidden markers: commit already
                                            reviewed, findings with an open thread), review-builder.ts (inline
                                            comments, findings off the diff in the body, the summary as markdown,
                                            the model's text sanitized), test/
    skills/<skill>/                         tools a skill runs through Bash, one folder per skill in .claude/skills/
                                            (the prompts stay there). One file per command, named after it; pure
                                            logic a command needs sits next to it, with its tests in test/.
                                            plan-ticket/: print-body-sha.ts (hash of the body before the skill reads
                                            it), publish-plan.ts (refuses a changed body, removes agent:plan-approved,
                                            edits the one plan comment in place).
                                            implement-ticket/: prepare-branch.ts (refuses without an approved plan for
                                            the current body or in a dirty checkout, switches to agent/issue-<n>,
                                            prints the plan), publish-pr.ts (checks the gate again, commits, pushes
                                            and opens the PR as the agent's GitHub App), implementation-gate.ts (the
                                            gate as a pure function: open with the plan, or closed with the reason),
                                            pull-request-text.ts (title and body from stdin, adds Closes #<n>), test/
docs/                                       domain.md, architecture.md, repository-layout.md, acceptance-scenarios.md, diagrams.md,
                                            adr/ (index: adr/README.md),
                                            images/ (screenshots for the README)
.github/workflows/ci.yml                    on PRs and main: Build job (build, format, .claude/hooks/check-rules.sh), then Test
                                            job ("Test results" check, coverage summary); Admin app job (npm ci, build);
                                            Agent flow tests job (npm ci, npm test in tools/agent-flow);
                                            on PRs that are ready and not from a fork, Agent review job after Build
                                            (pr-review with tools from main; not a required check). The "Protect main"
                                            ruleset requires the Build, Test and Admin app checks by job name: renaming a
                                            job blocks every merge
.claude/rules/                              topic rules, some load only for matching paths
.claude/skills/                             slash commands: explain-ticket (read-only explanation of a GitHub issue),
                                            review-pr (read-only review of a PR against the repo's rules; prints a
                                            summary and JSON findings anchored to diff lines, posts nothing;
                                            tools/agent-flow/flows/pr-review publishes them),
                                            plan-ticket (implementation plan of an issue, published as its one plan
                                            comment through tools/agent-flow/skills/plan-ticket; changes no files),
                                            implement-ticket (an approved plan implemented tests first on
                                            agent/issue-<n> in a clean checkout, then a PR)
.claude/settings.json                       permissions and hooks enforced by the harness
.claude/hooks/                              check-rules.sh: Teamtailor in Domain/Application, direct clock in src/
                                            (skips bin, obj, node_modules)
.gitattributes                              *.sh keeps LF line endings (Git Bash on Windows)
.dockerignore                               build context for images (repo root); excludes node_modules, .angular, dist
```
