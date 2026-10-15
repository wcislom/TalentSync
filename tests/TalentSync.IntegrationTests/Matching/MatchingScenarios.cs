using Microsoft.EntityFrameworkCore;
using TalentSync.Contracts;
using TalentSync.IntegrationTests.Infrastructure;

namespace TalentSync.IntegrationTests.Matching;

/// <summary>
/// Matching scenarios of <c>docs/acceptance-scenarios.md</c>. The LLM mode of a scenario is a marker in its
/// candidate's CV, never a switch on the shared host (ADR 007); model calls are attributed to a candidate by
/// <see cref="SystemFixture.CvTag"/>.
/// </summary>
[Collection(SystemCollection.Name)]
public sealed class MatchingScenarios(SystemFixture fixture)
{
    private const string Qualified = "Qualified";
    private const string Interview = "Interview";
    private const string Screening = "Screening";

    [Fact]
    public async Task M1_QualifiedApplicationCreatesAnEnrichedProfile()
    {
        const string candidateId = "c-m1";
        var application = await ApplyAsync(candidateId, "Valid", "Anna", "Lis");
        await MoveAndWaitAsync(candidateId, application, Interview);

        await fixture.FakeTeamtailor.ChangeStageAsync(application, Qualified);

        var profile = await Eventually.GetAsync(() => fixture.FakeMatchingPlatform.FindProfileAsync(candidateId), $"profile {candidateId}");
        Assert.Equal("Enriched", profile.EnrichmentStatus);
        Assert.NotEmpty(profile.Skills);
        Assert.NotNull(profile.Seniority);
        Assert.Equal(1, await CountEnrichmentResultsAsync(candidateId));
    }

    [Fact]
    public async Task M2_NonQualifiedStageCreatesNoProfile()
    {
        const string candidateId = "c-m2";
        var application = await ApplyAsync(candidateId, "Valid", "Bartosz", "Mazur");
        await MoveAndWaitAsync(candidateId, application, Screening);

        await fixture.FakeTeamtailor.ChangeStageAsync(application, Interview);
        var moved = await GetLatestEventIdAsync(candidateId, EventTypes.JobApplicationUpdate);

        // Not part of the Then: an absence is asserted only after the event is handled (testing rule).
        await WaitUntilHandledAsync(moved);

        Assert.Null(await fixture.FakeMatchingPlatform.FindProfileAsync(candidateId));
        Assert.Equal(0, fixture.CountModelCalls(candidateId));
    }

    [Fact]
    public async Task M3_LlmTimeoutYieldsANotEnrichedProfile()
    {
        const string candidateId = "c-m3";
        var application = await ApplyAsync(candidateId, "Hang", "Celina", "Zajac");
        await MoveAndWaitAsync(candidateId, application, Interview);

        await fixture.FakeTeamtailor.ChangeStageAsync(application, Qualified);

        var profile = await Eventually.GetAsync(() => fixture.FakeMatchingPlatform.FindProfileAsync(candidateId), $"profile {candidateId}");
        Assert.Equal("NotEnriched", profile.EnrichmentStatus);
        Assert.Equal(0, await CountEnrichmentResultsAsync(candidateId));
    }

    [Theory]
    [InlineData("c-m4a", "InvalidJson")]
    [InlineData("c-m4b", "OutOfRange")]
    public async Task M4_InvalidLlmOutputYieldsANotEnrichedProfile(string candidateId, string mode)
    {
        var application = await ApplyAsync(candidateId, mode, "Dawid", "Kowal");
        await MoveAndWaitAsync(candidateId, application, Interview);

        await fixture.FakeTeamtailor.ChangeStageAsync(application, Qualified);

        var profile = await Eventually.GetAsync(() => fixture.FakeMatchingPlatform.FindProfileAsync(candidateId), $"profile {candidateId}");
        Assert.Equal("NotEnriched", profile.EnrichmentStatus);
        Assert.Equal(0, await CountEnrichmentResultsAsync(candidateId));
    }

    [Fact]
    public async Task M5_SecondQualificationWithAnUnchangedCvReusesTheStoredEnrichment()
    {
        const string candidateId = "c-m5";
        var first = await ApplyAsync(candidateId, "Valid", "Ewa", "Sowa");
        await MoveAndWaitAsync(candidateId, first, Interview);
        await fixture.FakeTeamtailor.ChangeStageAsync(first, Qualified);
        var profile = await Eventually.GetAsync(() => fixture.FakeMatchingPlatform.FindProfileAsync(candidateId), $"profile {candidateId}");
        Assert.Equal("Enriched", profile.EnrichmentStatus);

        // An existing candidate keeps the CV: the second application does not change it.
        var second = await ApplyAsync(candidateId, "Valid", "Ewa", "Sowa");
        await MoveAndWaitAsync(candidateId, second, Interview);
        await fixture.FakeTeamtailor.ChangeStageAsync(second, Qualified);
        var secondQualified = await GetLatestEventIdAsync(candidateId, EventTypes.JobApplicationUpdate);

        // Not part of the Then: the counts below are final once the second qualification is handled.
        await WaitUntilHandledAsync(secondQualified);

        Assert.Equal(1, fixture.CountModelCalls(candidateId));
        var calls = await fixture.FakeMatchingPlatform.GetCallsAsync(candidateId);
        Assert.Equal(2, calls.CountOf("PUT", $"/profiles/{candidateId}"));
    }

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
        await MoveAndWaitAsync(candidateId, application, Qualified);
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

    [Fact]
    public async Task M8_CandidateUpdateIsIgnoredByMatching()
    {
        const string candidateId = "c-m8";
        var application = await ApplyAsync(candidateId, "Valid", "Filip", "Nowicki");
        await fixture.FakeTeamtailor.ChangeStageAsync(application, Qualified);
        var qualified = await GetLatestEventIdAsync(candidateId, EventTypes.JobApplicationUpdate);
        await WaitUntilHandledAsync(qualified);
        var putsBefore = (await fixture.FakeMatchingPlatform.GetCallsAsync(candidateId)).CountOf("PUT", $"/profiles/{candidateId}");
        var modelCallsBefore = fixture.CountModelCalls(candidateId);

        await fixture.FakeTeamtailor.UpdateEmailAsync(candidateId, "filip.nowicki.new@example.com");

        // Not part of the Then: candidate.update writes no inbox row, so a later handled event of the same candidate
        // (same partition, processed after it) is the ordering marker. A new application is in stage New: no PUT.
        await ApplyAsync(candidateId, "Valid", "Filip", "Nowicki");
        var marker = await GetLatestEventIdAsync(candidateId, EventTypes.JobApplicationCreate);
        await WaitUntilHandledAsync(marker);

        var putsAfter = (await fixture.FakeMatchingPlatform.GetCallsAsync(candidateId)).CountOf("PUT", $"/profiles/{candidateId}");
        Assert.Equal(putsBefore, putsAfter);
        Assert.Equal(modelCallsBefore, fixture.CountModelCalls(candidateId));
    }

    /// <summary>
    /// A Given step: moves the application and waits until Matching.Worker has handled the move. Events are a signal and
    /// the worker reads the current stage (ADR 003), so an earlier event handled after a later move acts on the later
    /// stage: without the wait, an event of the move to Interview could see Qualified and add a PUT the scenario does
    /// not expect. Earlier events of the candidate are on the same partition, so they are handled by then too.
    /// </summary>
    private async Task MoveAndWaitAsync(string candidateId, string jobApplicationId, string stage)
    {
        await fixture.FakeTeamtailor.ChangeStageAsync(jobApplicationId, stage);
        await WaitUntilHandledAsync(await GetLatestEventIdAsync(candidateId, EventTypes.JobApplicationUpdate));
    }

    /// <summary>Applies with a CV that picks the LLM mode and carries the candidate's tag.</summary>
    private Task<string> ApplyAsync(string candidateId, string llmMode, string firstName, string lastName) =>
        fixture.FakeTeamtailor.ApplyAsync(
            candidateId,
            firstName,
            lastName,
            $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}@example.com",
            $"{FakeTeamtailorTestClient.DefaultResumeText} {SystemFixture.CvTag(candidateId)} [llm:{llmMode}]");

    private Task<int> CountEnrichmentResultsAsync(string candidateId) =>
        fixture.QueryMatchingDbAsync(db => db.EnrichmentResults.CountAsync(r => r.CandidateId == candidateId));

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
