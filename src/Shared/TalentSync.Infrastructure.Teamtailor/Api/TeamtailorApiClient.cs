using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Infrastructure.Teamtailor.Api;

/// <summary>
/// The Teamtailor REST API in its own language: returns JSON:API DTOs and knows nothing about our model.
/// Classifies responses as in <c>.claude/rules/teamtailor.md</c>: 404 is <c>Deleted</c>, any other client error or an
/// unreadable body is <c>Rejected</c>, and failures of the environment (5xx, 408, 429, 401, 403) are thrown as
/// <see cref="HttpRequestException"/> for the handler loop to wait out. Retries live in the HTTP pipeline
/// (<c>AddTeamtailorApi</c>), not here.
/// </summary>
internal sealed class TeamtailorApiClient(HttpClient http)
{
    internal const string CandidatesEndpoint = "/v1/candidates";
    internal const string JobApplicationsEndpoint = "/v1/job-applications";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
    };

    public Task<FetchResult<CandidateResponseDto>> GetCandidateAsync(string candidateId, CancellationToken cancellationToken) =>
        GetAsync<CandidateResponseDto>(
            $"{CandidatesEndpoint}/{Uri.EscapeDataString(candidateId)}", CandidatesEndpoint, cancellationToken);

    public Task<FetchResult<JobApplicationResponseDto>> GetJobApplicationAsync(string jobApplicationId, CancellationToken cancellationToken) =>
        GetAsync<JobApplicationResponseDto>(
            $"{JobApplicationsEndpoint}/{Uri.EscapeDataString(jobApplicationId)}?include=stage", JobApplicationsEndpoint, cancellationToken);

    /// <param name="endpoint">The path without ids, for error reasons: ids stay out of <c>ParkingLot.Error</c>.</param>
    private async Task<FetchResult<TResponse>> GetAsync<TResponse>(string path, string endpoint, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await http.GetAsync(path, cancellationToken);

        var status = response.StatusCode;
        if (status == HttpStatusCode.NotFound)
        {
            // ASSUMPTION (teamtailor rule): 404 means the resource was deleted.
            return new FetchResult<TResponse>.Deleted();
        }

        if (IsEnvironmentFailure(status))
        {
            // The status code only: a response body may echo PII (invariant 14).
            throw new HttpRequestException($"GET {endpoint} answered {(int)status}", inner: null, status);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new FetchResult<TResponse>.Rejected($"{(int)status} GET {endpoint}");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken) is { } body
                ? new FetchResult<TResponse>.Found(body)
                : new FetchResult<TResponse>.Rejected($"empty body from GET {endpoint}");
        }
        catch (JsonException)
        {
            return new FetchResult<TResponse>.Rejected($"unreadable JSON from GET {endpoint}");
        }
    }

    // 401 and 403 are a wrong key or a missing header: every event would fail the same way, so they wait for a
    // configuration fix instead of being parked one by one.
    private static bool IsEnvironmentFailure(HttpStatusCode status) =>
        (int)status >= 500
        || status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden;
}
