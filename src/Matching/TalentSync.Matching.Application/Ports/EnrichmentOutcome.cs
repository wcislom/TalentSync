using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Application.Ports;

/// <summary>What <see cref="IEnrichmentService"/> got from the model.</summary>
public abstract record EnrichmentOutcome
{
    private EnrichmentOutcome()
    {
    }

    /// <summary>A valid suggestion. <paramref name="ModelId"/> is stored with it for audit only.</summary>
    public sealed record Enriched(Enrichment Enrichment, string ModelId) : EnrichmentOutcome;

    /// <summary>No usable suggestion. A reason, not the model's text, so no output can reach a log (invariant 14).</summary>
    public sealed record Failed(EnrichmentFailure Reason) : EnrichmentOutcome;
}

public enum EnrichmentFailure
{
    /// <summary>The model did not answer within the configured timeout.</summary>
    Timeout,

    /// <summary>The model could not be called: an error from the provider (including 429) or an open circuit.</summary>
    Unavailable,

    /// <summary>The answer was not valid JSON of the expected shape, or broke a domain rule.</summary>
    InvalidOutput,
}
