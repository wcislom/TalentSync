namespace TalentSync.Matching.Domain.Tests;

public sealed class ConsultantProfileTests
{
    [Fact]
    public void New_IsNotEnrichedAndKeyedByCandidateId()
    {
        var profile = new ConsultantProfile(new CandidateId("c-1"));

        Assert.Equal(new CandidateId("c-1"), profile.CandidateId);
        Assert.Equal(EnrichmentStatus.NotEnriched, profile.EnrichmentStatus);
        Assert.Empty(profile.Skills);
        Assert.Null(profile.Seniority);
        Assert.Null(profile.Confidence);
    }

    [Fact]
    public void Enrich_TakesSkillsSeniorityAndConfidenceFromTheSuggestion()
    {
        var profile = new ConsultantProfile(new CandidateId("c-1"));

        profile.Enrich(Suggestion(["C#"], "Lead", 0.9));

        Assert.Equal(EnrichmentStatus.Enriched, profile.EnrichmentStatus);
        Assert.Equal([new Skill("C#")], profile.Skills);
        Assert.Equal(Seniority.Lead, profile.Seniority);
        Assert.Equal(new Confidence(0.9), profile.Confidence);
    }

    [Fact]
    public void Enrich_AlreadyEnriched_Throws()
    {
        var profile = new ConsultantProfile(new CandidateId("c-1"));
        profile.Enrich(Suggestion(["C#"], "Lead", 0.9));

        Assert.Throws<InvalidOperationException>(() => profile.Enrich(Suggestion(["Go"], "Mid", 0.5)));
    }

    private static EnrichmentSuggestion Suggestion(string[] skills, string seniority, double confidence) =>
        Assert.IsType<EnrichmentSuggestionValidation.ValidResult>(EnrichmentSuggestion.Validate(skills, seniority, confidence)).Suggestion;
}
