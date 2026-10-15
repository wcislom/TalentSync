namespace TalentSync.Matching.Infrastructure.EnrichmentResults;

/// <summary>A suggestion as the model gave it for one CV of a candidate. Never updated: a changed CV is a new row.</summary>
public sealed class EnrichmentResult
{
    public long Id { get; private set; }

    /// <summary>Together with <see cref="CvHash"/> the unique key; a duplicate insert means another processor stored one first.</summary>
    public required string CandidateId { get; init; }

    public required string CvHash { get; init; }

    /// <summary>JSON: skills, seniority, confidence.</summary>
    public required string Result { get; init; }

    /// <summary>For audit only, not part of the key: a new model reuses the suggestions stored by the old one.</summary>
    public required string ModelId { get; init; }

    /// <summary>UTC, from <see cref="TimeProvider"/>.</summary>
    public required DateTime CreatedAt { get; init; }
}
