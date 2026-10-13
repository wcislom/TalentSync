using System.Net.Http.Json;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>Drives FakeTeamtailor's recruiter and test endpoints. Each state change sends its webhook to Ingress.</summary>
public sealed class FakeTeamtailorTestClient(HttpClient http)
{
    public const string RateLimit = "rate-limit";
    public const string BadRequest = "bad-request";
    public const string DuplicateWebhook = "duplicate-webhook";
    public const string DropWebhook = "drop-webhook";

    private const string JobId = "job-1";

    /// <summary>
    /// Applies to a job in stage New. A new candidate id creates the candidate; an existing one adds an application.
    /// </summary>
    /// <returns>The id of the new job application.</returns>
    public async Task<string> ApplyAsync(string candidateId, string firstName, string lastName, string email)
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
        var applied = await response.Content.ReadFromJsonAsync<Applied>(TestContext.Current.CancellationToken);
        return applied!.JobApplicationId;
    }

    public async Task ChangeStageAsync(string jobApplicationId, string stage)
    {
        using var response = await http.PatchAsJsonAsync(
            $"/recruiter/job-applications/{jobApplicationId}/stage", new { stage }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateEmailAsync(string candidateId, string email)
    {
        using var response = await http.PatchAsJsonAsync($"/recruiter/candidates/{candidateId}", new { email }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteCandidateAsync(string candidateId)
    {
        using var response = await http.DeleteAsync($"/recruiter/candidates/{candidateId}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Switches a failure mode on for this candidate only, until cleared or for <paramref name="seconds"/>.</summary>
    public async Task SetAnomalyAsync(string mode, string candidateId, int? seconds = null, int? count = null)
    {
        using var response = await http.PostAsJsonAsync("/anomalies", new { mode, candidateId, seconds, count }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearAnomaliesAsync(string candidateId)
    {
        using var response = await http.DeleteAsync($"/anomalies?candidateId={candidateId}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SimulatorCall>> GetCallsAsync(string candidateId) =>
        await http.GetFromJsonAsync<List<SimulatorCall>>($"/calls?candidateId={candidateId}", TestContext.Current.CancellationToken)
        ?? [];

    private sealed record Applied(string CandidateId, string JobApplicationId);
}
