using System.Net;
using System.Net.Http.Json;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Infrastructure.MatchingPlatform;

/// <summary>
/// <see cref="IMatchingPlatform"/> over the Matching Platform's REST API. Success is <c>Done</c>, an unexpected client
/// error is <c>Rejected</c>, and failures of the environment (5xx, 408, 429, 401, 403) are thrown as
/// <see cref="HttpRequestException"/> for the handler loop to wait out. Retries live in the HTTP pipeline
/// (<c>AddMatchingInfrastructure</c>), not here.
/// </summary>
internal sealed class MatchingPlatformClient(HttpClient http) : IMatchingPlatform
{
    internal const string ProfilesEndpoint = "/profiles";

    public async Task<MatchingPlatformResult> UpsertProfileAsync(ConsultantProfile profile, CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync(
            $"{ProfilesEndpoint}/{Uri.EscapeDataString(profile.CandidateId)}",
            MatchingPlatformMapper.ToProfileRequest(profile),
            cancellationToken);

        var status = response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            return MatchingPlatformResult.Done;
        }

        if (IsEnvironmentFailure(status))
        {
            // The status code only: a response body may echo what was sent (invariant 14).
            throw new HttpRequestException($"PUT {ProfilesEndpoint} answered {(int)status}", inner: null, status);
        }

        return MatchingPlatformResult.Rejected($"{(int)status} PUT {ProfilesEndpoint}");
    }

    // Same split as the CRM and Teamtailor clients. 401 and 403 are a wrong key or a missing header: every event
    // would fail the same way, so they wait for a configuration fix instead of being parked one by one.
    private static bool IsEnvironmentFailure(HttpStatusCode status) =>
        (int)status >= 500
        || status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden;
}
