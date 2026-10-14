using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Matching.Application.UseCases;

/// <summary>
/// Creates or updates the consultant profile when a job application is in the qualified stage (D2, D3).
/// </summary>
/// <remarks>
/// Takes the application id, not the event type: the fetched state decides (notify-then-fetch, ADR 003), so a
/// duplicate or reordered event repeats an idempotent upsert. An application moved back from qualified, or deleted,
/// leaves the profile as it is: the Matching Platform owns it (D4).
/// </remarks>
public sealed class CreateConsultantProfileUseCase(IRecruitmentSource recruitmentSource, IMatchingPlatform matchingPlatform)
{
    public async Task<CreateConsultantProfileResult> ExecuteAsync(string jobApplicationId, CancellationToken cancellationToken)
    {
        var fetched = await recruitmentSource.GetJobApplicationAsync(jobApplicationId, cancellationToken);

        switch (fetched)
        {
            case FetchResult<JobApplication>.Found { Value.Status: ApplicationStatus.Qualified } found:
                // The candidate id comes from the source, not from the event: the API state is the truth.
                var profile = ConsultantProfile.NotEnriched(new CandidateId(found.Value.CandidateId));
                return await matchingPlatform.UpsertProfileAsync(profile, cancellationToken) switch
                {
                    MatchingPlatformResult.DoneResult => CreateConsultantProfileResult.Upserted,
                    MatchingPlatformResult.RejectedResult rejected => CreateConsultantProfileResult.Rejected(rejected.Reason),
                    _ => throw new InvalidOperationException("Unknown Matching Platform result."),
                };

            case FetchResult<JobApplication>.Found or FetchResult<JobApplication>.Deleted:
                return CreateConsultantProfileResult.Skipped;

            case FetchResult<JobApplication>.Rejected rejected:
                return CreateConsultantProfileResult.Rejected(rejected.Reason);

            default:
                throw new InvalidOperationException("Unknown fetch result.");
        }
    }
}
