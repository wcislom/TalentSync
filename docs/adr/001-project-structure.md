# ADR 001: Project structure

## Status

Accepted

## Context

TalentSync has three deployables, split by runtime behaviour: Ingress.Api, Sync.Worker and Matching.Worker. They
share code and are released together. The code covers two bounded contexts, Recruitment (a read-only model of a
third-party SaaS) and Consultant Matching (a model with behaviour), and must keep Teamtailor details out of the
use cases. Rules that live only in documents are easy to break, so the compiler should enforce as many as possible.

## Decision

Clean Architecture, with the hosts as the presentation layer (driving adapters and composition root).

- **Split by context first, then by layer** (`src/Shared`, `src/Ingress`, `src/Sync`, `src/Matching`).
- **A project wherever the compiler should enforce a rule.** Project references carry the dependency rules in
  `docs/architecture.md`: `*.Domain` and `Contracts` reference nothing, `*.Application` references only domain
  projects.
- **No Common project.** Shared code lives in projects named after what they hold: `Contracts`,
  `Recruitment.Domain`, `Infrastructure.Teamtailor`, `Infrastructure.Messaging`, each with its own dependency rules.
- **Port `IRecruitmentSource` lives in `Recruitment.Domain`**, named after the need. The adapter
  `TeamtailorRecruitmentSource` lives in `Infrastructure.Teamtailor`, which only hosts reference. Implementations
  and JSON:API DTOs are `internal`; the public surface is `AddTeamtailor()` plus the webhook verifier and parser
  interfaces and the parser's result type.
- **`Matching.Domain` does not reference `Recruitment.Domain`.** The two contexts do not share models;
  `Matching.Application` translates between them.
- **Deliberate asymmetries:** Sync has no business rules, so there is no `Sync.Domain`. Ingress has no domain and
  is one project; its infrastructure lives inside `Ingress.Api`.
- Each worker's `DbContext` lives in its own `*.Infrastructure`, so the other worker cannot see its schema.

## Consequences

- The compiler enforces that use cases do not know HTTP, JSON:API or Teamtailor, that JSON:API types never leave
  the adapter, that the bounded contexts do not share models, and that a worker does not touch another worker's
  schema.
- Consumer invariants (inbox, checkpoint, never throw, parking lot) are implemented once, in
  `Infrastructure.Messaging`, and reused by both workers.
- **Cost: more projects** than a layer-first or single-project layout, and more references to keep right.
- **Cost: translation code** in `Matching.Application` between the recruitment and matching models.
- The recruitment model looks anemic on purpose: its rules live in Teamtailor and we only read it.
- Some rules cannot be expressed as project references. The word "Teamtailor" in `*.Domain` or `*.Application`
  and direct clock use in `src/` are reported by a harness hook (`.claude/hooks/check-rules.sh`) after every edit.
  Architecture tests are out of MVP scope.

## Alternatives considered

- **A Common project for shared code.** Rejected: it would merge projects with different dependency rules
  (`Contracts` references nothing, Ingress does not reference `Infrastructure.Messaging`, only hosts reference
  `Infrastructure.Teamtailor`), so the compiler could no longer enforce them.
- **The same layers in every context** (a `Sync.Domain`, a layered Ingress). Rejected: Sync has no business rules
  and Ingress has no domain, so these projects would be empty.
