namespace TalentSync.Matching.Application.Ports;

/// <summary>
/// Asks the LLM for a suggestion of skills and seniority (D5). The adapter owns the prompt, the masking of the CV and
/// the parsing; the use case sees only the outcome.
/// </summary>
public interface IEnrichmentService
{
    /// <summary>
    /// Never throws for a failure of the model or of its output: those are <see cref="EnrichmentOutcome.Failed"/>,
    /// so they end in a <c>NotEnriched</c> profile instead of a retried event. Throws only when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<EnrichmentOutcome> EnrichAsync(string cvText, CancellationToken cancellationToken);
}
