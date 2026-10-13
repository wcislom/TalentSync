using TalentSync.Recruitment.Domain;
using TalentSync.Sync.Application.Ports;
using TalentSync.Sync.Application.UseCases;

namespace TalentSync.Sync.Application.Tests;

public sealed class SyncCandidateToCrmUseCaseTests
{
    private const string CandidateId = "c-1";

    private static readonly Candidate Candidate = new(CandidateId, "Alicja", "Nowak", "alicja.nowak@example.com", null, "CV");

    private readonly RecordingCrm _crm = new();

    [Fact]
    public async Task FoundCandidate_IsUpsertedAndNotDeleted()
    {
        var result = await Execute(new FetchResult<Candidate>.Found(Candidate));

        Assert.IsType<SyncCandidateToCrmResult.DoneResult>(result);
        Assert.Same(Candidate, Assert.Single(_crm.Upserted));
        Assert.Empty(_crm.Deleted);
    }

    [Fact]
    public async Task DeletedCandidate_IsDeletedAndNotUpserted()
    {
        var result = await Execute(new FetchResult<Candidate>.Deleted());

        Assert.IsType<SyncCandidateToCrmResult.DoneResult>(result);
        Assert.Equal(CandidateId, Assert.Single(_crm.Deleted));
        Assert.Empty(_crm.Upserted);
    }

    [Fact]
    public async Task RejectedFetch_IsRejectedWithItsReasonAndTouchesNoContact()
    {
        var result = await Execute(new FetchResult<Candidate>.Rejected("400 /v1/candidates"));

        Assert.Equal("400 /v1/candidates", Assert.IsType<SyncCandidateToCrmResult.RejectedResult>(result).Reason);
        Assert.Empty(_crm.Upserted);
        Assert.Empty(_crm.Deleted);
    }

    [Fact]
    public async Task RejectedUpsert_IsRejectedWithItsReason()
    {
        _crm.Result = CrmResult.Rejected("422 /contacts");

        var result = await Execute(new FetchResult<Candidate>.Found(Candidate));

        Assert.Equal("422 /contacts", Assert.IsType<SyncCandidateToCrmResult.RejectedResult>(result).Reason);
    }

    [Fact]
    public async Task RejectedDelete_IsRejectedWithItsReason()
    {
        _crm.Result = CrmResult.Rejected("409 /contacts");

        var result = await Execute(new FetchResult<Candidate>.Deleted());

        Assert.Equal("409 /contacts", Assert.IsType<SyncCandidateToCrmResult.RejectedResult>(result).Reason);
    }

    private Task<SyncCandidateToCrmResult> Execute(FetchResult<Candidate> fetched) =>
        new SyncCandidateToCrmUseCase(new StubRecruitmentSource(fetched), _crm)
            .ExecuteAsync(CandidateId, TestContext.Current.CancellationToken);

    private sealed class StubRecruitmentSource(FetchResult<Candidate> candidate) : IRecruitmentSource
    {
        public Task<FetchResult<Candidate>> GetCandidateAsync(string candidateId, CancellationToken cancellationToken) =>
            Task.FromResult(candidate);

        public Task<FetchResult<JobApplication>> GetJobApplicationAsync(string jobApplicationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sync reads candidates only.");
    }

    private sealed class RecordingCrm : ICrm
    {
        public CrmResult Result { get; set; } = CrmResult.Done;

        public List<Candidate> Upserted { get; } = [];

        public List<string> Deleted { get; } = [];

        public Task<CrmResult> UpsertContactAsync(Candidate candidate, CancellationToken cancellationToken)
        {
            Upserted.Add(candidate);
            return Task.FromResult(Result);
        }

        public Task<CrmResult> DeleteContactAsync(string candidateId, CancellationToken cancellationToken)
        {
            Deleted.Add(candidateId);
            return Task.FromResult(Result);
        }
    }
}
