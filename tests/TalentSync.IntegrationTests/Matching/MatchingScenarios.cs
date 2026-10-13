using Microsoft.EntityFrameworkCore;
using TalentSync.Contracts;
using TalentSync.IntegrationTests.Infrastructure;

namespace TalentSync.IntegrationTests.Matching;

/// <summary>
/// Scenarios of <c>docs/acceptance-scenarios.md</c> that need no LLM: M6 and M7. M1–M5 and M8 come with LLM
/// enrichment, because their Then counts <c>FakeChatClient</c> calls.
/// </summary>
[Collection(SystemCollection.Name)]
public sealed class MatchingScenarios(SystemFixture fixture)
{
    private const string Qualified = "Qualified";
    private const string Interview = "Interview";

    [Fact]
    public async Task M6_SecondQualifiedApplicationUpdatesTheSameProfile()
    {
        const string candidateId = "c-m6";
        var first = await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Hanna", "Pawlak", "hanna.pawlak@example.com");
        var second = await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Hanna", "Pawlak", "hanna.pawlak@example.com");
        await fixture.FakeTeamtailor.ChangeStageAsync(first, Interview);
        await fixture.FakeTeamtailor.ChangeStageAsync(second, Interview);

        await fixture.FakeTeamtailor.ChangeStageAsync(first, Qualified);
        var firstQualified = await GetLatestEventIdAsync(candidateId, EventTypes.JobApplicationUpdate);
        await fixture.FakeTeamtailor.ChangeStageAsync(second, Qualified);
        var secondQualified = await GetLatestEventIdAsync(candidateId, EventTypes.JobApplicationUpdate);

        // Not part of the Then: both qualifications must be handled before the profiles are counted.
        await WaitUntilHandledAsync(firstQualified);
        await WaitUntilHandledAsync(secondQualified);

        var profiles = await fixture.FakeMatchingPlatform.GetProfilesAsync();
        Assert.Single(profiles, profile => profile.CandidateId == candidateId);
    }

    [Fact]
    public async Task M7_MovingBackFromQualifiedLeavesTheProfileUnchanged()
    {
        const string candidateId = "c-m7";
        var application = await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Igor", "Wrobel", "igor.wrobel@example.com");
        await fixture.FakeTeamtailor.ChangeStageAsync(application, Qualified);
        var before = await Eventually.GetAsync(() => fixture.FakeMatchingPlatform.FindProfileAsync(candidateId), $"profile {candidateId}");
        var putsBefore = (await fixture.FakeMatchingPlatform.GetCallsAsync(candidateId)).CountOf("PUT", $"/profiles/{candidateId}");

        await fixture.FakeTeamtailor.ChangeStageAsync(application, Interview);
        var movedBack = await GetLatestEventIdAsync(candidateId, EventTypes.JobApplicationUpdate);

        // Not part of the Then: the inbox row is written after the event is handled, so the state below is final.
        await WaitUntilHandledAsync(movedBack);

        var putsAfter = (await fixture.FakeMatchingPlatform.GetCallsAsync(candidateId)).CountOf("PUT", $"/profiles/{candidateId}");
        Assert.Equal(putsBefore, putsAfter);
        Assert.Equivalent(before, await fixture.FakeMatchingPlatform.FindProfileAsync(candidateId), strict: true);
    }

    /// <summary>
    /// The candidate's most recent event of this type. Ingress stores the webhook before it answers, and FakeTeamtailor
    /// waits for that answer, so the row exists once the recruiter call has returned.
    /// </summary>
    private Task<string> GetLatestEventIdAsync(string candidateId, string eventType) =>
        fixture.QueryIngressDbAsync(db => db.Outbox
            .Where(m => m.CandidateId == candidateId && m.Type == eventType)
            .OrderByDescending(m => m.Id)
            .Select(m => m.EventId)
            .FirstAsync());

    private Task WaitUntilHandledAsync(string eventId) =>
        Eventually.GetAsync(
            () => fixture.QueryMatchingDbAsync(db => db.Inbox.FirstOrDefaultAsync(e => e.EventId == eventId)),
            $"matching.Inbox row for {eventId}");
}
