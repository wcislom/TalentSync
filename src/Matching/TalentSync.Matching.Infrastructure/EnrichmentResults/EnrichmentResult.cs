namespace TalentSync.Matching.Infrastructure.EnrichmentResults;

/// <summary>
/// A suggestion as the model gave it for one CV of a candidate and one prompt. Never updated: a changed CV or prompt is
/// a new row.
/// </summary>
public sealed class EnrichmentResult
{
    public long Id { get; private set; }

    /// <summary>Together with <see cref="CvHash"/> and <see cref="PromptVersion"/> the unique key; a duplicate insert means another processor stored one first.</summary>
    public required string CandidateId { get; init; }

    public required string CvHash { get; init; }

    public required string PromptVersion { get; init; }

    /// <summary>JSON: skills, seniority, confidence.</summary>
    public required string Result { get; init; }

    /// <summary>
    /// For audit only, not part of the key: a new model reuses the suggestions stored by the old one, a new prompt
    /// (<see cref="PromptVersion"/>) does not.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>UTC, from <see cref="TimeProvider"/>.</summary>
    public required DateTime CreatedAt { get; init; }
}
