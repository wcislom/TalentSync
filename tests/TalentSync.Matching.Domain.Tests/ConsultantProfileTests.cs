namespace TalentSync.Matching.Domain.Tests;

public sealed class ConsultantProfileTests
{
    [Fact]
    public void CreateFromQualifiedCandidate_WithoutSuggestion_IsNotEnrichedAndKeyedByCandidateId()
    {
        var profile = ConsultantProfile.CreateFromQualifiedCandidate("c-1");

        Assert.Equal("c-1", profile.CandidateId);
        Assert.Equal(EnrichmentStatus.NotEnriched, profile.EnrichmentStatus);
        Assert.Empty(profile.Skills);
        Assert.Null(profile.Seniority);
        Assert.Null(profile.Confidence);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateFromQualifiedCandidate_WithoutCandidateId_Throws(string candidateId) =>
        Assert.Throws<ArgumentException>(() => ConsultantProfile.CreateFromQualifiedCandidate(candidateId));
}
