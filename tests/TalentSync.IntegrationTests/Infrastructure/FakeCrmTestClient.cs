using System.Net.Http.Json;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>A contact as FakeCrm's <c>GET /contacts</c> returns it.</summary>
public sealed record CrmContact(string CandidateId, string FirstName, string LastName, string Email, string? Phone, string Status);

/// <summary>Reads FakeCrm's end state and call counts, and breaks it on demand.</summary>
public sealed class FakeCrmTestClient(HttpClient http)
{
    public const string Unavailable = "503";

    /// <summary>Switches a failure mode on for this candidate only, for <paramref name="seconds"/>.</summary>
    public async Task SetAnomalyAsync(string mode, string candidateId, int seconds)
    {
        using var response = await http.PostAsJsonAsync("/anomalies", new { mode, candidateId, seconds }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <returns>Null while the contact does not exist, so it can be polled with <see cref="Eventually"/>.</returns>
    public async Task<CrmContact?> FindContactAsync(string candidateId)
    {
        var contacts = await http.GetFromJsonAsync<List<CrmContact>>("/contacts", TestContext.Current.CancellationToken) ?? [];
        return contacts.SingleOrDefault(contact => contact.CandidateId == candidateId);
    }

    public async Task<IReadOnlyList<SimulatorCall>> GetCallsAsync(string candidateId) =>
        await http.GetFromJsonAsync<List<SimulatorCall>>($"/calls?candidateId={candidateId}", TestContext.Current.CancellationToken)
        ?? [];
}
