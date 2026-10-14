namespace TalentSync.Matching.Domain.Tests;

public sealed class ConsultantProfileTests
{
    [Fact]
    public void NotEnriched_HasNoEnrichmentAndIsKeyedByCandidateId()
    {
        var profile = ConsultantProfile.NotEnriched(new CandidateId("c-1"));

        Assert.Equal(new CandidateId("c-1"), profile.CandidateId);
        Assert.Null(profile.Enrichment);
        Assert.Equal(EnrichmentStatus.NotEnriched, profile.EnrichmentStatus);
    }

    [Fact]
    public void Enriched_CarriesTheEnrichment()
    {
        var enrichment = new Enrichment([new Skill("C#")], Seniority.Lead, new Confidence(0.9));

        var profile = ConsultantProfile.Enriched(new CandidateId("c-1"), enrichment);

        Assert.Same(enrichment, profile.Enrichment);
        Assert.Equal(EnrichmentStatus.Enriched, profile.EnrichmentStatus);
    }

    [Fact]
    public void Enriched_WithoutEnrichment_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ConsultantProfile.Enriched(new CandidateId("c-1"), null!));
}
