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

    public IReadOnlyList<Skill> Skills { get; private set; } = [];

    public Seniority? Seniority { get; private set; }

    /// <summary>Confidence of the suggestion. Null without one.</summary>
    public Confidence? Confidence { get; private set; }

    public EnrichmentStatus EnrichmentStatus { get; private set; } = EnrichmentStatus.NotEnriched;

    /// <summary>Puts a validated LLM suggestion on the profile. A suggestion, never a match decision (D5).</summary>
    /// <remarks>
    /// Allowed once. A profile is built afresh for each qualification and carries the single suggestion stored for
    /// the current CV (ADR 007); a second call would mean two answers for one CV.
    /// </remarks>
    public void Enrich(EnrichmentSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (EnrichmentStatus == EnrichmentStatus.Enriched)
        {
            throw new InvalidOperationException("The profile is already enriched.");
        }

        Skills = suggestion.Skills;
        Seniority = suggestion.Seniority;
        Confidence = suggestion.Confidence;
        EnrichmentStatus = EnrichmentStatus.Enriched;
    }
}
