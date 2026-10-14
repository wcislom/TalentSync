namespace TalentSync.Matching.Domain;

/// <summary>
/// The outcome of <see cref="Enrichment.TryCreate"/>. Invalid model output is an expected outcome, not a bug, so it is
/// a value: the use case turns it into a <see cref="EnrichmentStatus.NotEnriched"/> profile (D5).
/// </summary>
public abstract record EnrichmentValidation
{
    private EnrichmentValidation()
    {
    }

    public sealed record Valid(Enrichment Enrichment) : EnrichmentValidation;

    /// <summary>An enum, not text, so the model output can never end up in a log (invariant 14).</summary>
    public sealed record Invalid(EnrichmentRejection Reason) : EnrichmentValidation;
}
