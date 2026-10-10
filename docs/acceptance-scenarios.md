# Acceptance scenarios

Normative. Each scenario is a concrete example of a domain rule (`docs/domain.md`, D1–D6) or an invariant
(`.claude/rules/invariants.md`, 1–17); the `Illustrates` line says which. A rule says what must be true in general;
a scenario shows how to check it in one case. When a rule changes, search for its ID here.

- Every automated scenario has exactly one integration test named after its ID (see `.claude/rules/testing.md`).
- Scenarios tagged `@manual` are demonstrated by hand and have no test.
- Never change a Then to make a test pass. If a scenario looks wrong, say so and stop.

**Choosing the assertion.** A Then must fail if the mechanism under test were missing.
An idempotent upsert gives one record even without any deduplication, so deduplication is asserted on the number of
calls (`/calls`), not on the number of records. A crash between an effect and the inbox write legitimately repeats
the effect, so crash recovery is asserted on records, not on calls.

Every scenario uses its own candidate ID, so `/calls` counts per candidate need no reset between tests.

## Sync (Sync.Worker → CRM)

```gherkin
# Illustrates: D1
Scenario: S1 New candidate creates CRM contact
  Given candidate "c-s1" exists in FakeTeamtailor
  When FakeTeamtailor sends candidate.create for "c-s1"
  Then FakeCrm has a contact for "c-s1" with the candidate's name and email

# Illustrates: invariants 1, 2, 3
Scenario: S2 Duplicate webhook produces one CRM call
  Given candidate "c-s2" exists in FakeTeamtailor
  When FakeTeamtailor delivers the same candidate.create webhook for "c-s2" 3 times
  Then FakeCrm /calls shows exactly 1 PUT /contacts/c-s2
  And sync.Inbox has exactly 1 row for that EventId

# Illustrates: D1
Scenario: S3 Deleted candidate marks the CRM contact as deleted
  Given candidate "c-s3" has a contact in FakeCrm
  When candidate "c-s3" is deleted in FakeTeamtailor
  # FakeTeamtailor sends candidate.destroy and /v1/candidates/c-s3 returns 404
  Then FakeCrm /calls shows 1 DELETE /contacts/c-s3
  And FakeCrm has a contact for "c-s3" with status Deleted
  And sync.ParkingLot has no row for that EventId

# Illustrates: invariant 8 (transient)
Scenario: S4 CRM outage delays but does not lose events
  Given FakeCrm returns 503 for the next 30 seconds
  And candidate "c-s4" exists in FakeTeamtailor
  When FakeTeamtailor sends candidate.create for "c-s4"
  Then within 90 seconds FakeCrm has a contact for "c-s4"
  And sync.ParkingLot has no row for that EventId

# Illustrates: invariant 8 (transient), Teamtailor rate limit
Scenario: S5 Teamtailor rate limit is respected
  Given FakeTeamtailor /v1 returns 429 for the next 5 seconds with X-Rate-Limit-Reset
  And candidate "c-s5" exists in FakeTeamtailor
  When FakeTeamtailor sends candidate.create for "c-s5"
  Then FakeCrm eventually has a contact for "c-s5"
  And FakeTeamtailor /calls shows exactly 2 GET /v1/candidates/c-s5
  # one answered 429, one after the reset; more calls mean the worker ignored X-Rate-Limit-Reset

# Illustrates: invariants 7, 8 (permanent)
Scenario: S6 Poison event is parked and the partition continues
  Given candidate "c-s6" exists in FakeTeamtailor
  And FakeTeamtailor /v1 returns 400 for "c-s6"
  When FakeTeamtailor sends candidate.create for "c-s6"
  And FakeTeamtailor /v1 is healthy again
  And FakeTeamtailor sends candidate.update for "c-s6"
  # same candidate = same partition, so the second event can only be processed if the first did not block it
  Then sync.ParkingLot has exactly 1 row, for the candidate.create EventId
  And FakeCrm has a contact for "c-s6"

# Illustrates: invariants 1, 2, 3, 6
@manual
Scenario: S7 Killed worker resumes without duplicate records
  Given candidate "c-s7" exists in FakeTeamtailor
  And Sync.Worker is paused after the CRM PUT and before the inbox insert (breakpoint or delay)
  When FakeTeamtailor sends candidate.create for "c-s7"
  And Sync.Worker is killed and restarted
  Then FakeCrm has exactly 1 contact for "c-s7"
  And FakeCrm /calls shows at least 1 PUT /contacts/c-s7
  # 2 PUTs is correct behaviour: at-least-once delivery, made safe by the idempotent upsert

# Illustrates: D1, invariant 2
Scenario: S8 Deleting a candidate without a CRM contact succeeds
  Given candidate "c-s8" exists in FakeTeamtailor and has no contact in FakeCrm
  When candidate "c-s8" is deleted in FakeTeamtailor
  # FakeCrm answers DELETE /contacts/c-s8 with 404: the contact counts as already deleted
  Then FakeCrm /calls shows 1 DELETE /contacts/c-s8
  And sync.ParkingLot has no row for that EventId
  And sync.Inbox has exactly 1 row for that EventId
```

## Matching (Matching.Worker → Matching Platform)

LLM behaviour is set with `Llm:Provider = Fake` and `Llm:Fake:Mode`. `FakeChatClient` counts its calls.

```gherkin
# Illustrates: D2, D5, invariant 5
Scenario: M1 Qualified application creates an enriched profile
  Given candidate "c-m1" has an application in stage Interview
  And the LLM mode is Valid
  When the application is moved to Qualified
  Then FakeMatchingPlatform has a profile for "c-m1" with status Enriched, skills and seniority
  And matching.EnrichmentResults has exactly 1 row for "c-m1"

# Illustrates: D2
Scenario: M2 Non-qualified stage creates no profile
  Given candidate "c-m2" has an application in stage Screening
  When the application is moved to Interview
  Then FakeMatchingPlatform has no profile for "c-m2"
  And FakeChatClient was not called for "c-m2"

# Illustrates: D5, invariant 16
Scenario: M3 LLM timeout yields a NotEnriched profile
  Given candidate "c-m3" has an application in stage Interview
  And the LLM mode is Hang
  When the application is moved to Qualified
  Then FakeMatchingPlatform has a profile for "c-m3" with status NotEnriched
  And matching.EnrichmentResults has no row for "c-m3"

# Illustrates: D5, invariant 16
Scenario Outline: M4 Invalid LLM output yields a NotEnriched profile
  Given candidate "<candidate>" has an application in stage Interview
  And the LLM mode is <mode>
  When the application is moved to Qualified
  Then FakeMatchingPlatform has a profile for "<candidate>" with status NotEnriched
  And matching.EnrichmentResults has no row for "<candidate>"

  Examples:
    | candidate | mode        |
    | c-m4a     | InvalidJson |
    | c-m4b     | OutOfRange  |

# Illustrates: invariant 5
Scenario: M5 Second qualification with an unchanged CV reuses the stored enrichment
  Given candidate "c-m5" has a profile with status Enriched from a first qualified application
  And the LLM mode is Valid
  When a second application of "c-m5" is moved to Qualified and the CV is unchanged
  Then FakeChatClient was called exactly once in total for "c-m5"
  And FakeMatchingPlatform /calls shows exactly 2 PUT /profiles/c-m5
  # equal skills would prove nothing: FakeChatClient returns the same output every time

# Illustrates: D3
Scenario: M6 Second qualified application updates the same profile
  Given candidate "c-m6" has two applications in stage Interview
  When both applications are moved to Qualified
  Then GET /profiles in FakeMatchingPlatform returns exactly 1 profile for "c-m6"

# Illustrates: D4
Scenario: M7 Moving back from qualified leaves the profile unchanged
  Given candidate "c-m7" has a profile from a qualified application
  When the application is moved from Qualified to Interview
  Then FakeMatchingPlatform /calls shows no PUT /profiles/c-m7 after the move
  And the profile for "c-m7" is unchanged

# Illustrates: D6
Scenario: M8 Candidate update is ignored by Matching
  Given candidate "c-m8" has a profile from a qualified application
  When FakeTeamtailor sends candidate.update for "c-m8"
  Then FakeMatchingPlatform /calls shows no PUT /profiles/c-m8 after the update
  And FakeChatClient was not called again for "c-m8"
  # candidate.* is skipped without an inbox row; wait with an ordering marker (see testing rule)
```

## Ingress

Done last in the MVP (signature v2). The test sends the request to Ingress itself.

```gherkin
# Illustrates: Teamtailor signature v2
Scenario: I1 Invalid signature is rejected
  When a webhook with a TT-Signature computed with the wrong key is sent to Ingress
  Then Ingress responds 401
  And ingress.Outbox has no row for that EventId

# Illustrates: Teamtailor signature v2 (replay window)
Scenario: I2 Stale timestamp is rejected
  When a correctly signed webhook with a timestamp 6 minutes in the past is sent to Ingress
  Then Ingress responds 401
  And ingress.Outbox has no row for that EventId
```
