using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Application.UseCases;
using TalentSync.Matching.Domain;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Matching.Application.Tests;

public sealed class CreateConsultantProfileUseCaseTests
{
    private const string JobApplicationId = "ja-1";

    private readonly RecordingMatchingPlatform _platform = new();

    [Fact]
    public async Task QualifiedApplication_UpsertsANotEnrichedProfileKeyedByTheFetchedCandidateId()
    {
        var result = await Execute(Found(ApplicationStatus.Qualified, candidateId: "c-from-api"));

        Assert.IsType<CreateConsultantProfileResult.UpsertedResult>(result);
        var profile = Assert.Single(_platform.Upserted);
        Assert.Equal(new CandidateId("c-from-api"), profile.CandidateId);
        Assert.Equal(EnrichmentStatus.NotEnriched, profile.EnrichmentStatus);
    }

    [Fact]
    public async Task NotQualifiedApplication_IsSkippedWithoutUpsert()
    {
        var result = await Execute(Found(ApplicationStatus.NotQualified));

        Assert.IsType<CreateConsultantProfileResult.SkippedResult>(result);
        Assert.Empty(_platform.Upserted);
    }

    [Fact]
    public async Task DeletedApplication_IsSkippedWithoutUpsert()
    {
        var result = await Execute(new FetchResult<JobApplication>.Deleted());

        Assert.IsType<CreateConsultantProfileResult.SkippedResult>(result);
        Assert.Empty(_platform.Upserted);
    }

    [Fact]
    public async Task RejectedFetch_IsRejectedWithItsReasonWithoutUpsert()
    {
        var result = await Execute(new FetchResult<JobApplication>.Rejected("400 /v1/job-applications"));

        Assert.Equal("400 /v1/job-applications", Assert.IsType<CreateConsultantProfileResult.RejectedResult>(result).Reason);
        Assert.Empty(_platform.Upserted);
    }

    [Fact]
    public async Task RejectedUpsert_IsRejectedWithItsReason()
    {
        _platform.Result = MatchingPlatformResult.Rejected("422 /profiles");

        var result = await Execute(Found(ApplicationStatus.Qualified));

        Assert.Equal("422 /profiles", Assert.IsType<CreateConsultantProfileResult.RejectedResult>(result).Reason);
    }

    private static FetchResult<JobApplication> Found(ApplicationStatus status, string candidateId = "c-1") =>
        new FetchResult<JobApplication>.Found(new JobApplication(JobApplicationId, candidateId, status));

    private Task<CreateConsultantProfileResult> Execute(FetchResult<JobApplication> fetched) =>
        new CreateConsultantProfileUseCase(new StubRecruitmentSource(fetched), _platform)
            .ExecuteAsync(JobApplicationId, TestContext.Current.CancellationToken);

    private sealed class StubRecruitmentSource(FetchResult<JobApplication> jobApplication) : IRecruitmentSource
    {
        public Task<FetchResult<Candidate>> GetCandidateAsync(string candidateId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Without enrichment the use case reads the application only.");

        public Task<FetchResult<JobApplication>> GetJobApplicationAsync(string jobApplicationId, CancellationToken cancellationToken) =>
            Task.FromResult(jobApplication);
    }

    private sealed class RecordingMatchingPlatform : IMatchingPlatform
    {
        public MatchingPlatformResult Result { get; set; } = MatchingPlatformResult.Done;

        public List<ConsultantProfile> Upserted { get; } = [];

        public Task<MatchingPlatformResult> UpsertProfileAsync(ConsultantProfile profile, CancellationToken cancellationToken)
        {
            Upserted.Add(profile);
            return Task.FromResult(Result);
        }
    }
}
