using System.Net.Http.Json;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>Drives FakeTeamtailor's recruiter and test endpoints. Each state change sends its webhook to Ingress.</summary>
public sealed class FakeTeamtailorTestClient(HttpClient http)
{
    private const string JobId = "job-1";

    public async Task ApplyAsync(string candidateId, string firstName, string lastName, string email)
    {
        using var response = await http.PostAsJsonAsync($"/recruiter/jobs/{JobId}/apply", new
        {
            candidateId,
            firstName,
            lastName,
            email,
            phone = "+48 600 000 000",
            resumeText = "Backend developer, 6 years of .NET.",
        }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SimulatorCall>> GetCallsAsync(string candidateId) =>
        await http.GetFromJsonAsync<List<SimulatorCall>>($"/calls?candidateId={candidateId}", TestContext.Current.CancellationToken)
        ?? [];
}
