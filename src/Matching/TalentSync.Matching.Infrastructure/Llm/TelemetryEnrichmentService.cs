using TalentSync.Matching.Application.Ports;

namespace TalentSync.Matching.Infrastructure.Llm;

/// <summary>
/// Decorates <see cref="IEnrichmentService"/> with an <c>enrich</c> span and a counter of outcomes (ADR 010). Under the
/// span, the <c>chat</c> span of <c>OpenTelemetryChatClient</c> separates the provider's answer from our validation:
/// <c>invalid_output</c> with a successful <c>chat</c> span means the model answered and our rules rejected it.
/// </summary>
/// <remarks>
/// The use case calls the service only when no stored suggestion exists, so a reused suggestion has no span: in a
/// trace, a profile upsert without an <c>enrich</c> span before it means the stored suggestion was used.
/// </remarks>
internal sealed class TelemetryEnrichmentService(IEnrichmentService inner, MatchingTelemetry telemetry) : IEnrichmentService
{
    public string PromptVersion => inner.PromptVersion;

    public async Task<EnrichmentOutcome> EnrichAsync(string cvText, CancellationToken cancellationToken)
    {
        using var activity = telemetry.StartEnrichment(inner.PromptVersion);
        var outcome = await inner.EnrichAsync(cvText, cancellationToken);
        telemetry.EndEnrichment(activity, outcome);
        return outcome;
    }
}
