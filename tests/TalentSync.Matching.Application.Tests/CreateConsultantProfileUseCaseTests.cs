using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Application.UseCases;
using TalentSync.Matching.Domain;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Matching.Application.Tests;

public sealed class CreateConsultantProfileUseCaseTests
{
    private const string JobApplicationId = "ja-1";
    private const string Cv = "Backend developer, C#, SQL Server.";

    private static readonly Enrichment Suggestion = new([new Skill("C#")], Seniority.Senior, new Confidence(0.8));

    // Every stub writes to one log, so a test can assert the order of the steps.
    private readonly List<string> _steps = [];
    private readonly StubRecruitmentSource _source;
    private readonly StubEnrichmentService _model;
    private readonly RecordingEnrichmentResultStore _store;
    private readonly RecordingMatchingPlatform _platform;

    public CreateConsultantProfileUseCaseTests()
    {
        _source = new StubRecruitmentSource(Found(ApplicationStatus.Qualified), FoundCandidate(Cv));
        _model = new StubEnrichmentService(_steps);
        _store = new RecordingEnrichmentResultStore(_steps);
        _platform = new RecordingMatchingPlatform(_steps);
    }

    [Fact]
    public async Task QualifiedApplication_UpsertsAProfileKeyedByTheFetchedCandidateId()
    {
        _source.JobApplication = Found(ApplicationStatus.Qualified, candidateId: "c-from-api");

        var result = await Execute();

        Assert.IsType<CreateConsultantProfileResult.UpsertedResult>(result);
        Assert.Equal("c-from-api", _source.RequestedCandidateId);
        Assert.Equal(new CandidateId("c-from-api"), Assert.Single(_platform.Upserted).CandidateId);
    }

    [Fact]
    public async Task ValidSuggestion_IsStoredBeforeTheEnrichedProfileIsUpserted()
    {
        var result = await Execute();

        Assert.IsType<CreateConsultantProfileResult.UpsertedResult>(result);
        Assert.Equal(["find", "enrich", "save", "upsert"], _steps);
        var saved = Assert.Single(_store.Saved);
        Assert.Equal(CvHash.Of(Cv), saved.CvHash);
        Assert.Equal("model-1", saved.ModelId);
        var profile = Assert.Single(_platform.Upserted);
        Assert.Equal(EnrichmentStatus.Enriched, profile.EnrichmentStatus);
        Assert.Same(Suggestion, profile.Enrichment);
    }

    [Fact]
    public async Task StoredSuggestion_IsReusedWithoutCallingTheModel()
    {
        _store.Stored = Suggestion;

        await Execute();

        Assert.Equal(["find", "upsert"], _steps);
        Assert.Same(Suggestion, Assert.Single(_platform.Upserted).Enrichment);
    }

    [Fact]
    public async Task SuggestionStoredByAnotherProcessorFirst_IsTheOneUpserted()
    {
        var theirs = new Enrichment([new Skill("Java")], Seniority.Mid, new Confidence(0.6));
        _store.StoredOnSave = theirs;

        await Execute();

        Assert.Same(theirs, Assert.Single(_platform.Upserted).Enrichment);
    }

    [Theory]
    [InlineData(EnrichmentFailure.Timeout)]
    [InlineData(EnrichmentFailure.Unavailable)]
    [InlineData(EnrichmentFailure.InvalidOutput)]
    public async Task FailedEnrichment_UpsertsANotEnrichedProfileAndStoresNothing(EnrichmentFailure failure)
    {
        _model.Outcome = new EnrichmentOutcome.Failed(failure);

        var result = await Execute();

        Assert.IsType<CreateConsultantProfileResult.UpsertedResult>(result);
        Assert.Empty(_store.Saved);
        Assert.Equal(EnrichmentStatus.NotEnriched, Assert.Single(_platform.Upserted).EnrichmentStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task EmptyCv_UpsertsANotEnrichedProfileWithoutCallingTheModel(string? cv)
    {
        _source.Candidate = FoundCandidate(cv);

        await Execute();

        Assert.Equal(["upsert"], _steps);
        Assert.Equal(EnrichmentStatus.NotEnriched, Assert.Single(_platform.Upserted).EnrichmentStatus);
    }

    [Fact]
    public async Task NotQualifiedApplication_IsSkippedWithoutReadingTheCandidate()
    {
        _source.JobApplication = Found(ApplicationStatus.NotQualified);

        var result = await Execute();

        Assert.IsType<CreateConsultantProfileResult.SkippedResult>(result);
        Assert.Null(_source.RequestedCandidateId);
        Assert.Empty(_steps);
    }

    [Fact]
    public async Task DeletedApplication_IsSkippedWithoutUpsert()
    {
        _source.JobApplication = new FetchResult<JobApplication>.Deleted();

        var result = await Execute();

        Assert.IsType<CreateConsultantProfileResult.SkippedResult>(result);
        Assert.Empty(_steps);
    }

    [Fact]
    public async Task RejectedApplicationFetch_IsRejectedWithItsReasonWithoutUpsert()
    {
        _source.JobApplication = new FetchResult<JobApplication>.Rejected("400 /v1/job-applications");

        var result = await Execute();

        Assert.Equal("400 /v1/job-applications", Assert.IsType<CreateConsultantProfileResult.RejectedResult>(result).Reason);
        Assert.Empty(_steps);
    }

    [Fact]
    public async Task DeletedCandidate_IsSkippedWithoutEnrichmentOrUpsert()
    {
        _source.Candidate = new FetchResult<Candidate>.Deleted();

        var result = await Execute();

        Assert.IsType<CreateConsultantProfileResult.SkippedResult>(result);
        Assert.Empty(_steps);
    }

    [Fact]
    public async Task RejectedCandidateFetch_IsRejectedWithItsReasonWithoutEnrichmentOrUpsert()
    {
        _source.Candidate = new FetchResult<Candidate>.Rejected("400 /v1/candidates");

        var result = await Execute();

        Assert.Equal("400 /v1/candidates", Assert.IsType<CreateConsultantProfileResult.RejectedResult>(result).Reason);
        Assert.Empty(_steps);
    }

    [Fact]
    public async Task RejectedUpsert_IsRejectedWithItsReason()
    {
        _platform.Result = MatchingPlatformResult.Rejected("422 /profiles");

        var result = await Execute();

        Assert.Equal("422 /profiles", Assert.IsType<CreateConsultantProfileResult.RejectedResult>(result).Reason);
    }

    private static FetchResult<JobApplication> Found(ApplicationStatus status, string candidateId = "c-1") =>
        new FetchResult<JobApplication>.Found(new JobApplication(JobApplicationId, candidateId, status));

    private static FetchResult<Candidate> FoundCandidate(string? cv) =>
        new FetchResult<Candidate>.Found(new Candidate("c-1", "Alicja", "Nowak", "alicja.nowak@example.com", null, cv));

    private Task<CreateConsultantProfileResult> Execute() =>
        new CreateConsultantProfileUseCase(_source, _model, _store, _platform)
            .ExecuteAsync(JobApplicationId, TestContext.Current.CancellationToken);

    private sealed class StubRecruitmentSource(FetchResult<JobApplication> jobApplication, FetchResult<Candidate> candidate)
        : IRecruitmentSource
    {
        public FetchResult<JobApplication> JobApplication { get; set; } = jobApplication;

        public FetchResult<Candidate> Candidate { get; set; } = candidate;

        public string? RequestedCandidateId { get; private set; }

        public Task<FetchResult<Candidate>> GetCandidateAsync(string candidateId, CancellationToken cancellationToken)
        {
            RequestedCandidateId = candidateId;
            return Task.FromResult(Candidate);
        }

        public Task<FetchResult<JobApplication>> GetJobApplicationAsync(string jobApplicationId, CancellationToken cancellationToken) =>
            Task.FromResult(JobApplication);
    }

    private sealed class StubEnrichmentService(List<string> steps) : IEnrichmentService
    {
        public EnrichmentOutcome Outcome { get; set; } = new EnrichmentOutcome.Enriched(Suggestion, "model-1");

        public Task<EnrichmentOutcome> EnrichAsync(string cvText, CancellationToken cancellationToken)
        {
            steps.Add("enrich");
            return Task.FromResult(Outcome);
        }
    }

    private sealed class RecordingEnrichmentResultStore(List<string> steps) : IEnrichmentResultStore
    {
        public Enrichment? Stored { get; set; }

        /// <summary>Set to simulate a duplicate key: another processor stored this one first.</summary>
        public Enrichment? StoredOnSave { get; set; }

        public List<(CandidateId CandidateId, CvHash CvHash, string ModelId)> Saved { get; } = [];

        public Task<Enrichment?> FindAsync(CandidateId candidateId, CvHash cvHash, CancellationToken cancellationToken)
        {
            steps.Add("find");
            return Task.FromResult(Stored);
        }

        public Task<Enrichment> SaveAsync(
            CandidateId candidateId, CvHash cvHash, Enrichment enrichment, string modelId, CancellationToken cancellationToken)
        {
            steps.Add("save");
            Saved.Add((candidateId, cvHash, modelId));
            return Task.FromResult(StoredOnSave ?? enrichment);
        }
    }

    private sealed class RecordingMatchingPlatform(List<string> steps) : IMatchingPlatform
    {
        public MatchingPlatformResult Result { get; set; } = MatchingPlatformResult.Done;

        public List<ConsultantProfile> Upserted { get; } = [];

        public Task<MatchingPlatformResult> UpsertProfileAsync(ConsultantProfile profile, CancellationToken cancellationToken)
        {
            steps.Add("upsert");
            Upserted.Add(profile);
            return Task.FromResult(Result);
        }
    }
}
