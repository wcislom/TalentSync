using System.Net;
using System.Net.Http.Json;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>A profile as FakeMatchingPlatform's <c>GET /profiles</c> returns it.</summary>
public sealed record MatchingProfile(
    string CandidateId, IReadOnlyList<string> Skills, string? Seniority, double? Confidence, string EnrichmentStatus, DateTimeOffset UpdatedAt);

/// <summary>Reads FakeMatchingPlatform's end state and call counts.</summary>
public sealed class FakeMatchingPlatformTestClient(HttpClient http)
{
    public async Task<IReadOnlyList<MatchingProfile>> GetProfilesAsync() =>
        await http.GetFromJsonAsync<List<MatchingProfile>>("/profiles", TestContext.Current.CancellationToken) ?? [];

    /// <returns>Null while the profile does not exist, so it can be polled with <see cref="Eventually"/>.</returns>
    public async Task<MatchingProfile?> FindProfileAsync(string candidateId)
    {
        using var response = await http.GetAsync($"/profiles/{candidateId}", TestContext.Current.CancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MatchingProfile>(TestContext.Current.CancellationToken);
    }

    public async Task<IReadOnlyList<SimulatorCall>> GetCallsAsync(string candidateId) =>
        await http.GetFromJsonAsync<List<SimulatorCall>>($"/calls?candidateId={candidateId}", TestContext.Current.CancellationToken)
        ?? [];
}
