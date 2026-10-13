using System.Net.Http.Json;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>A contact as FakeCrm's <c>GET /contacts</c> returns it.</summary>
public sealed record CrmContact(string CandidateId, string FirstName, string LastName, string Email, string? Phone, string Status);

/// <summary>Reads FakeCrm's end state and call counts.</summary>
public sealed class FakeCrmTestClient(HttpClient http)
{
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
