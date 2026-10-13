using TalentSync.IntegrationTests.Infrastructure;

namespace TalentSync.IntegrationTests.Sync;

/// <summary>Scenarios S1–S8 of <c>docs/acceptance-scenarios.md</c> (S7 is manual).</summary>
[Collection(SystemCollection.Name)]
public sealed class SyncScenarios(SystemFixture fixture)
{
    [Fact]
    public async Task S1_NewCandidateCreatesCrmContact()
    {
        const string candidateId = "c-s1";

        await fixture.FakeTeamtailor.ApplyAsync(candidateId, "Alicja", "Nowak", "alicja.nowak@example.com");

        var contact = await Eventually.GetAsync(() => fixture.FakeCrm.FindContactAsync(candidateId), $"CRM contact {candidateId}");
        Assert.Equal("Alicja", contact.FirstName);
        Assert.Equal("Nowak", contact.LastName);
        Assert.Equal("alicja.nowak@example.com", contact.Email);
    }
}
