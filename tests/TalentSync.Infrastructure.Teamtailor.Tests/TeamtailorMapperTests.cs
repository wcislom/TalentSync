using TalentSync.Infrastructure.Teamtailor.Api;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Infrastructure.Teamtailor.Tests;

public class TeamtailorMapperTests
{
    private const string QualifiedStageName = "Qualified";

    [Fact]
    public void Candidate_IsMappedWithCv()
    {
        var response = new CandidateResponseDto(new CandidateDto(
            "c-1", new CandidateAttributesDto("Jane", "Doe", "jane@example.com", "+48 600 000 000", "Ten years of .NET")));

        var result = TeamtailorMapper.ToCandidate(Found(response));

        var found = Assert.IsType<FetchResult<Candidate>.Found>(result);
        Assert.Equal(new Candidate("c-1", "Jane", "Doe", "jane@example.com", "+48 600 000 000", "Ten years of .NET"), found.Value);
    }

    [Fact]
    public void Candidate_WithoutAttributes_IsMappedWithEmptyFields()
    {
        var result = TeamtailorMapper.ToCandidate(Found(new CandidateResponseDto(new CandidateDto("c-1", Attributes: null))));

        Assert.Equal(new Candidate("c-1", null, null, null, null, null), Assert.IsType<FetchResult<Candidate>.Found>(result).Value);
    }

    [Fact]
    public void Candidate_WithoutId_IsRejected()
    {
        var result = TeamtailorMapper.ToCandidate(Found(new CandidateResponseDto(new CandidateDto(Id: null, Attributes: null))));

        Assert.Equal("unmappable GET /v1/candidates: data.id", Assert.IsType<FetchResult<Candidate>.Rejected>(result).Reason);
    }

    [Fact]
    public void DeletedAndRejected_PassThrough()
    {
        Assert.IsType<FetchResult<Candidate>.Deleted>(TeamtailorMapper.ToCandidate(new FetchResult<CandidateResponseDto>.Deleted()));

        var rejected = TeamtailorMapper.ToCandidate(new FetchResult<CandidateResponseDto>.Rejected("400 GET /v1/candidates"));

        Assert.Equal("400 GET /v1/candidates", Assert.IsType<FetchResult<Candidate>.Rejected>(rejected).Reason);
    }

    [Theory]
    [InlineData("Qualified", ApplicationStatus.Qualified)]
    [InlineData("qualified", ApplicationStatus.Qualified)]
    [InlineData("Interview", ApplicationStatus.NotQualified)]
    public void JobApplication_StageIsMappedFromConfiguration(string stageName, ApplicationStatus expected)
    {
        var response = JobApplication(stageId: "st-4", Stage("st-9", "Other"), Stage("st-4", stageName));

        var result = TeamtailorMapper.ToJobApplication(Found(response), QualifiedStageName);

        var found = Assert.IsType<FetchResult<JobApplication>.Found>(result);
        Assert.Equal(new JobApplication("ja-1", "c-1", expected), found.Value);
    }

    [Fact]
    public void JobApplication_WithoutIncludedStage_IsRejected()
    {
        var result = TeamtailorMapper.ToJobApplication(Found(JobApplication(stageId: "st-4")), QualifiedStageName);

        Assert.Equal("unmappable GET /v1/job-applications: included stage name", Assert.IsType<FetchResult<JobApplication>.Rejected>(result).Reason);
    }

    [Fact]
    public void JobApplication_WithoutCandidate_IsRejected()
    {
        var response = new JobApplicationResponseDto(
            new JobApplicationDto("ja-1", new JobApplicationRelationshipsDto(Candidate: null, Relationship("st-4", "stages"))),
            [Stage("st-4", QualifiedStageName)]);

        var result = TeamtailorMapper.ToJobApplication(Found(response), QualifiedStageName);

        Assert.Equal("unmappable GET /v1/job-applications: data.relationships.candidate", Assert.IsType<FetchResult<JobApplication>.Rejected>(result).Reason);
    }

    private static FetchResult<T> Found<T>(T response)
        where T : notnull => new FetchResult<T>.Found(response);

    private static JobApplicationResponseDto JobApplication(string stageId, params IncludedResourceDto[] included) => new(
        new JobApplicationDto("ja-1", new JobApplicationRelationshipsDto(Relationship("c-1", "candidates"), Relationship(stageId, "stages"))),
        included);

    private static RelationshipDto Relationship(string id, string type) => new(new ResourceIdentifierDto(id, type));

    private static IncludedResourceDto Stage(string id, string name) => new(id, "stages", new IncludedAttributesDto(name));
}
