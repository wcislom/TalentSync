using System.Diagnostics;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Infrastructure.Teamtailor.Api;

/// <summary>
/// Translates what <see cref="TeamtailorApiClient"/> returned into the recruitment model. <c>Deleted</c> and
/// <c>Rejected</c> pass through; a DTO that lacks what the model needs is <c>Rejected</c> with the missing field's
/// name only.
/// </summary>
internal static class TeamtailorMapper
{
    private const string StageType = "stages";

    public static FetchResult<Candidate> ToCandidate(FetchResult<CandidateResponseDto> fetched) =>
        Map(fetched, ToCandidate);

    /// <param name="qualifiedStageName">The customer-defined stage that means qualified (D2), from configuration.</param>
    public static FetchResult<JobApplication> ToJobApplication(FetchResult<JobApplicationResponseDto> fetched, string qualifiedStageName) =>
        Map(fetched, response => ToJobApplication(response, qualifiedStageName));

    private static FetchResult<T> Map<TResponse, T>(FetchResult<TResponse> fetched, Func<TResponse, FetchResult<T>> map)
        where TResponse : notnull
        where T : notnull =>
        fetched switch
        {
            FetchResult<TResponse>.Found found => map(found.Value),
            FetchResult<TResponse>.Deleted => new FetchResult<T>.Deleted(),
            FetchResult<TResponse>.Rejected rejected => new FetchResult<T>.Rejected(rejected.Reason),
            _ => throw new UnreachableException($"Unknown fetch result {fetched.GetType().Name}."),
        };

    private static FetchResult<Candidate> ToCandidate(CandidateResponseDto response)
    {
        if (response.Data is not { Id: { Length: > 0 } id } data)
        {
            return Unmappable<Candidate>(TeamtailorApiClient.CandidatesEndpoint, "data.id");
        }

        var attributes = data.Attributes;
        return new FetchResult<Candidate>.Found(new Candidate(
            id, attributes?.FirstName, attributes?.LastName, attributes?.Email, attributes?.Phone, attributes?.ResumeText));
    }

    private static FetchResult<JobApplication> ToJobApplication(JobApplicationResponseDto response, string qualifiedStageName)
    {
        const string endpoint = TeamtailorApiClient.JobApplicationsEndpoint;

        if (response.Data is not { Id: { Length: > 0 } id } data)
        {
            return Unmappable<JobApplication>(endpoint, "data.id");
        }

        if (data.Relationships?.Candidate?.Data?.Id is not { Length: > 0 } candidateId)
        {
            return Unmappable<JobApplication>(endpoint, "data.relationships.candidate");
        }

        if (data.Relationships.Stage?.Data?.Id is not { Length: > 0 } stageId)
        {
            return Unmappable<JobApplication>(endpoint, "data.relationships.stage");
        }

        // ASSUMPTION (teamtailor rule): include=stage puts the stage, with its name, into "included".
        var stageName = response.Included?.FirstOrDefault(r => r.Type == StageType && r.Id == stageId)?.Attributes?.Name;
        if (string.IsNullOrEmpty(stageName))
        {
            return Unmappable<JobApplication>(endpoint, "included stage name");
        }

        var status = string.Equals(stageName, qualifiedStageName, StringComparison.OrdinalIgnoreCase)
            ? ApplicationStatus.Qualified
            : ApplicationStatus.NotQualified;
        return new FetchResult<JobApplication>.Found(new JobApplication(id, candidateId, status));
    }

    private static FetchResult<T> Unmappable<T>(string endpoint, string field)
        where T : notnull =>
        new FetchResult<T>.Rejected($"unmappable GET {endpoint}: {field}");
}
