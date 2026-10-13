# ADR 008: Source of truth and sync direction

## Status

Accepted

## Context

Candidate data lives in three systems owned by different people (`docs/domain.md`):

- **Teamtailor**: recruiters and candidates edit the candidate and their job applications.
- **CRM**: sales keeps the relationship with the consultant on the contact.
- **Matching Platform**: account managers work with the consultant's profile once the candidate is qualified.

TalentSync has no users of its own. It is a courier and a translator between these systems. Each system can be
edited by people at any time, so a sync that writes in more than one direction must decide whose edit wins, and
must avoid echoing its own writes back. Domain rules D1, D3 and D4 already fix parts of the answer; this ADR
records the rule behind them and what it costs.

## Decision

**One direction: Teamtailor → TalentSync → CRM and Matching Platform.**

- TalentSync reads only from Teamtailor (notify-then-fetch, ADR 003) and writes only to the CRM and the Matching
  Platform.
- It never reads from the CRM or the Matching Platform, and never writes to Teamtailor.

**Ownership is per field, not per record.**

| Data | Owner | What TalentSync does |
|---|---|---|
| Candidate identity and personal data (name, email, phone, CV) | Teamtailor | Copies it to the CRM contact on every `candidate.*` event |
| Relationship data on the contact | CRM | Never sends it. Not modelled in the simulators |
| Contact status after a delete | CRM | Asks for a soft delete; the record stays (D1) |
| Consultant profile after qualification | Matching Platform | Writes it only on qualification (D2, D3); a move back from qualified changes nothing (D4) |
| The key that ties the records together | Teamtailor (`candidateId`) | Uses it as the key of every effect (D3, ADR 005) |

**Each write sends the full set of fields TalentSync owns and replaces them: last write wins.**

- `PUT /contacts/{candidateId}` replaces name, email and phone with the current Teamtailor state.
- `PUT /profiles/{candidateId}` replaces skills, seniority, confidence and enrichment status with the current
  result of the qualification flow (ADR 007).

## Consequences

- **No conflict resolution and no sync loops.** Nothing is ever read back, so no write of ours can come back as
  a change.
- **An edit in the CRM to a field Teamtailor owns is overwritten** by the next `candidate.*` event. Sales must
  correct personal data in Teamtailor, not in the CRM. A change made in the CRM never reaches recruitment.
- **The Matching Platform owns the profile, but a new qualification overwrites it** (D3). An account manager's
  edits to the fields TalentSync sends are lost when another application of the same candidate is qualified.
  Edits are not modelled in the simulators.
- **A profile can be downgraded.** If the CV changed and enrichment fails on a later qualification, a
  `NotEnriched` profile replaces an `Enriched` one. With an unchanged CV the stored suggestion is reused and the
  profile stays `Enriched` (ADR 005, ADR 007). Accepted for the MVP.
- **A deleted contact is reactivated by a later upsert.** Sync.Worker only upserts a candidate that Teamtailor
  still returns, so this happens only if the candidate exists again in Teamtailor, which is then the truth.
- **A deleted candidate keeps their profile** in the Matching Platform. Archiving and anonymisation are out of MVP
  scope (see Known limitations in `docs/architecture.md`).
- **Data in the CRM and the Matching Platform can lag behind Teamtailor**: while an event waits in a partition
  during an outage, or after an event lost at ingress (ADR 004). A reconciliation job would close the second gap;
  it is out of MVP scope.

## Alternatives considered

- **Two-way sync between Teamtailor and the CRM.** Rejected: it needs conflict resolution per field, a way to
  recognise and drop our own echoed writes, and write access to Teamtailor. TalentSync has no business rule that
  needs it.
- **Partial updates (PATCH only the fields that changed).** Rejected: the event carries no changed fields
  (ADR 003), so knowing what changed would need a stored copy of the previous state. A full replace of the fields
  we own is idempotent and needs no state.
- **Never downgrade an `Enriched` profile**: on enrichment failure, reuse the candidate's last stored suggestion,
  even one from an older CV. Rejected for the MVP: it is a new domain rule, and it would put skills from an old CV
  on a profile while hiding that enrichment of the current CV failed.
- **Treat the first write as final for the profile** (create once, never update). Rejected: contradicts D3, and a
  candidate qualified again with a new CV would keep an outdated profile.
