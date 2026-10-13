namespace TalentSync.Matching.Domain;

/// <summary>
/// A qualified candidate as account managers see it in the Matching Platform. One per candidate, keyed by the
/// candidate id (D3); email and phone are not identity and are not part of the profile.
/// </summary>
public sealed class ConsultantProfile
{
    private ConsultantProfile(string candidateId, IReadOnlyList<Skill> skills, Seniority? seniority, double? confidence, EnrichmentStatus enrichmentStatus)
    {
        CandidateId = candidateId;
        Skills = skills;
        Seniority = seniority;
        Confidence = confidence;
        EnrichmentStatus = enrichmentStatus;
    }

    public string CandidateId { get; }

    public IReadOnlyList<Skill> Skills { get; }

    public Seniority? Seniority { get; }

    /// <summary>Confidence of the suggestion, in [0, 1]. Null without one.</summary>
    public double? Confidence { get; }

    public EnrichmentStatus EnrichmentStatus { get; }

    /// <summary>
    /// The profile of a candidate whose application reached the qualified stage (D2). Without a suggestion it is
    /// <see cref="EnrichmentStatus.NotEnriched"/>: the profile never waits for enrichment (D5).
    /// </summary>
    public static ConsultantProfile CreateFromQualifiedCandidate(string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        return new ConsultantProfile(candidateId, [], seniority: null, confidence: null, EnrichmentStatus.NotEnriched);
    }
}
