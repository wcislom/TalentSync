namespace TalentSync.Matching.Domain;

/// <summary>
/// A qualified candidate as account managers see it in the Matching Platform. One per candidate, keyed by the
/// candidate id (D3); email and phone are not identity and are not part of the profile.
/// </summary>
public sealed class ConsultantProfile
{
    /// <summary>
    /// A profile starts <see cref="EnrichmentStatus.NotEnriched"/> and stays so unless <see cref="Enrich"/> is called:
    /// it never waits for enrichment (D5).
    /// </summary>
    /// <remarks>
    /// Whether the candidate is qualified is not checked here: qualification is a recruitment concept this context
    /// does not know. The use case translating between the two contexts decides when a profile is created (D2).
    /// </remarks>
    public ConsultantProfile(CandidateId candidateId)
    {
        ArgumentNullException.ThrowIfNull(candidateId);
        CandidateId = candidateId;
    }

    public CandidateId CandidateId { get; }

    /// <summary>Null until <see cref="Enrich"/> is called.</summary>
    public Enrichment? Enrichment { get; private set; }

    /// <summary>Follows from <see cref="Enrichment"/>, so "enriched without skills or seniority" cannot exist.</summary>
    public EnrichmentStatus EnrichmentStatus => Enrichment is null ? EnrichmentStatus.NotEnriched : EnrichmentStatus.Enriched;

    /// <summary>Puts the LLM's suggestion on the profile. A suggestion, never a match decision (D5).</summary>
    /// <remarks>
    /// Allowed once. A profile is built afresh for each qualification and carries the single enrichment stored for
    /// the current CV (ADR 007); a second call would mean two answers for one CV.
    /// </remarks>
    public void Enrich(Enrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        if (Enrichment is not null)
        {
            throw new InvalidOperationException("The profile is already enriched.");
        }

        Enrichment = enrichment;
    }
}
