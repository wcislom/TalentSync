namespace TalentSync.Matching.Domain.Tests;

public sealed class ConsultantProfileTests
{
    private static readonly Enrichment Enrichment = new([new Skill("C#")], Seniority.Lead, new Confidence(0.9));

    [Fact]
    public void New_IsNotEnrichedAndKeyedByCandidateId()
    {
        var profile = new ConsultantProfile(new CandidateId("c-1"));

        Assert.Equal(new CandidateId("c-1"), profile.CandidateId);
        Assert.Null(profile.Enrichment);
        Assert.Equal(EnrichmentStatus.NotEnriched, profile.EnrichmentStatus);
    }

    [Fact]
    public void Enrich_PutsTheEnrichmentOnTheProfile()
    {
        var profile = new ConsultantProfile(new CandidateId("c-1"));

        profile.Enrich(Enrichment);

        Assert.Same(Enrichment, profile.Enrichment);
        Assert.Equal(EnrichmentStatus.Enriched, profile.EnrichmentStatus);
    }

    [Fact]
    public void Enrich_AlreadyEnriched_Throws()
    {
        var profile = new ConsultantProfile(new CandidateId("c-1"));
        profile.Enrich(Enrichment);

        Assert.Throws<InvalidOperationException>(
            () => profile.Enrich(new Enrichment([new Skill("Go")], Seniority.Mid, new Confidence(0.5))));
    }
}
