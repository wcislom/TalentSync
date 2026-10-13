namespace TalentSync.Matching.Domain;

/// <summary>
/// A qualified candidate as account managers see it in the Matching Platform. One per candidate, keyed by the
/// candidate id (D3); email and phone are not identity and are not part of the profile.
/// </summary>
public sealed class ConsultantProfile
{
    private ConsultantProfile(string candidateId)
    {
        CandidateId = candidateId;
    }

    public string CandidateId { get; }

    public IReadOnlyList<Skill> Skills { get; private set; } = [];

    public Seniority? Seniority { get; private set; }

    /// <summary>Confidence of the suggestion. Null without one.</summary>
    public Confidence? Confidence { get; private set; }

    public EnrichmentStatus EnrichmentStatus { get; private set; } = EnrichmentStatus.NotEnriched;

    /// <summary>
    /// The profile of a candidate whose application reached the qualified stage (D2). It starts
    /// <see cref="EnrichmentStatus.NotEnriched"/> and stays so unless <see cref="Enrich"/> is called: the profile never
    /// waits for enrichment (D5).
    /// </summary>
    public static ConsultantProfile CreateFromQualifiedCandidate(string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        return new ConsultantProfile(candidateId);
    }

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
