using System.Net;
using System.Net.Http.Json;
using TalentSync.Recruitment.Domain;
using TalentSync.Sync.Application.Ports;

namespace TalentSync.Sync.Infrastructure.Crm;

/// <summary>
/// <see cref="ICrm"/> over the CRM's REST API. Success is <c>Done</c>, an unexpected client error is <c>Rejected</c>,
/// and failures of the environment (5xx, 408, 429, 401, 403) are thrown as <see cref="HttpRequestException"/> for
/// the handler loop to wait out. Retries live in the HTTP pipeline (<c>AddSyncInfrastructure</c>), not here.
/// </summary>
internal sealed class CrmClient(HttpClient http) : ICrm
{
    internal const string ContactsEndpoint = "/contacts";

    public async Task<CrmResult> UpsertContactAsync(Candidate candidate, CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync(ContactPath(candidate.Id), CrmMapper.ToContactRequest(candidate), cancellationToken);
        return ToResult(response, HttpMethod.Put);
    }

    public async Task<CrmResult> DeleteContactAsync(string candidateId, CancellationToken cancellationToken)
    {
        using var response = await http.DeleteAsync(ContactPath(candidateId), cancellationToken);

        // D1: a contact that never existed counts as already deleted. Parking it instead would park every delete
        // that arrives before, or without, the candidate's create.
        return response.StatusCode == HttpStatusCode.NotFound ? CrmResult.Done : ToResult(response, HttpMethod.Delete);
    }

    private static string ContactPath(string candidateId) => $"{ContactsEndpoint}/{Uri.EscapeDataString(candidateId)}";

    private static CrmResult ToResult(HttpResponseMessage response, HttpMethod method)
    {
        var status = response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            return CrmResult.Done;
        }

        if (IsEnvironmentFailure(status))
        {
            // The status code only: a response body may echo PII (invariant 14).
            throw new HttpRequestException($"{method} {ContactsEndpoint} answered {(int)status}", inner: null, status);
        }

        return CrmResult.Rejected($"{(int)status} {method} {ContactsEndpoint}");
    }

    // Same split as the Teamtailor client. 401 and 403 are a wrong key or a missing header: every event would fail
    // the same way, so they wait for a configuration fix instead of being parked one by one.
    private static bool IsEnvironmentFailure(HttpStatusCode status) =>
        (int)status >= 500
        || status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden;
}
