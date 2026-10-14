namespace TalentSync.Matching.Domain;

/// <summary>
/// A qualified candidate as account managers see it in the Matching Platform. One per candidate, keyed by the
/// candidate id (D3); email and phone are not identity and are not part of the profile.
/// </summary>
/// <remarks>
/// Built whole for each qualification from the enrichment outcome for the current CV (ADR 007), so it has no
/// state transitions. Whether the candidate is qualified is not checked here: qualification is a recruitment
/// concept this context does not know. The use case translating between the two contexts decides when a profile
/// is created (D2).
/// </remarks>
public sealed class ConsultantProfile
{
    private ConsultantProfile(CandidateId candidateId, Enrichment? enrichment)
    {
        ArgumentNullException.ThrowIfNull(candidateId);
        CandidateId = candidateId;
        Enrichment = enrichment;
    }

    public CandidateId CandidateId { get; }

    /// <summary>The LLM's suggestion. Null on a <see cref="EnrichmentStatus.NotEnriched"/> profile.</summary>
    public Enrichment? Enrichment { get; }

    /// <summary>Follows from <see cref="Enrichment"/>, so "enriched without skills or seniority" cannot exist.</summary>
    public EnrichmentStatus EnrichmentStatus => Enrichment is null ? EnrichmentStatus.NotEnriched : EnrichmentStatus.Enriched;

    /// <summary>A profile carrying the LLM's suggestion. A suggestion, never a match decision (D5).</summary>
    public static ConsultantProfile Enriched(CandidateId candidateId, Enrichment enrichment)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        return new ConsultantProfile(candidateId, enrichment);
    }

    /// <summary>A profile without a suggestion: a valid profile, created when enrichment failed (D5).</summary>
    public static ConsultantProfile NotEnriched(CandidateId candidateId) => new(candidateId, null);
}
