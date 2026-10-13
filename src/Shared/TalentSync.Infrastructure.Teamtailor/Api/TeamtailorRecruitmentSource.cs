using Microsoft.Extensions.Options;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Infrastructure.Teamtailor.Api;

/// <summary>
/// The anti-corruption layer: <see cref="IRecruitmentSource"/> on top of the Teamtailor API. Fetches with
/// <see cref="TeamtailorApiClient"/>, translates with <see cref="TeamtailorMapper"/>.
/// </summary>
internal sealed class TeamtailorRecruitmentSource(TeamtailorApiClient api, IOptions<TeamtailorApiOptions> options) : IRecruitmentSource
{
    private readonly string _qualifiedStageName = options.Value.QualifiedStageName;

    public async Task<FetchResult<Candidate>> GetCandidateAsync(string candidateId, CancellationToken cancellationToken) =>
        TeamtailorMapper.ToCandidate(await api.GetCandidateAsync(candidateId, cancellationToken));

    public async Task<FetchResult<JobApplication>> GetJobApplicationAsync(string jobApplicationId, CancellationToken cancellationToken) =>
        TeamtailorMapper.ToJobApplication(await api.GetJobApplicationAsync(jobApplicationId, cancellationToken), _qualifiedStageName);
}
