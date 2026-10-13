namespace TalentSync.Matching.Domain.Tests;

public sealed class ConsultantProfileTests
{
    [Fact]
    public void CreateFromQualifiedCandidate_IsNotEnrichedAndKeyedByCandidateId()
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

    [Fact]
    public void Enrich_TakesSkillsSeniorityAndConfidenceFromTheSuggestion()
    {
        var profile = ConsultantProfile.CreateFromQualifiedCandidate("c-1");

        profile.Enrich(Suggestion(["C#"], "Lead", 0.9));

        Assert.Equal(EnrichmentStatus.Enriched, profile.EnrichmentStatus);
        Assert.Equal([new Skill("C#")], profile.Skills);
        Assert.Equal(Seniority.Lead, profile.Seniority);
        Assert.Equal(new Confidence(0.9), profile.Confidence);
    }

    [Fact]
    public void Enrich_AlreadyEnriched_Throws()
    {
        var profile = ConsultantProfile.CreateFromQualifiedCandidate("c-1");
        profile.Enrich(Suggestion(["C#"], "Lead", 0.9));

        Assert.Throws<InvalidOperationException>(() => profile.Enrich(Suggestion(["Go"], "Mid", 0.5)));
    }

    private static EnrichmentSuggestion Suggestion(string[] skills, string seniority, double confidence) =>
        Assert.IsType<EnrichmentSuggestionValidation.ValidResult>(EnrichmentSuggestion.Validate(skills, seniority, confidence)).Suggestion;
}
