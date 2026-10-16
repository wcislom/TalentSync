using System.Diagnostics;
using System.Diagnostics.Metrics;
using TalentSync.Matching.Application.Ports;

namespace TalentSync.Matching.Infrastructure.Llm;

/// <summary>
/// Decorates <see cref="IEnrichmentService"/> with an <c>enrich</c> span and a counter of outcomes (ADR 010).
/// </summary>
/// <remarks>
/// The use case calls the service only when no stored suggestion exists, so a reused suggestion has no span: in a
/// trace, a profile upsert without an <c>enrich</c> span before it means the stored suggestion was used.
/// </remarks>
internal sealed class TelemetryEnrichmentService(IEnrichmentService inner) : IEnrichmentService
{
    private const string OutcomeTag = "talentsync.enrichment.outcome";

    private static readonly Counter<long> Calls = MatchingTelemetry.Meter.CreateCounter<long>(
        "talentsync.enrichment.calls", description: "Enrichment attempts by outcome.");

    public string PromptVersion => inner.PromptVersion;

    public async Task<EnrichmentOutcome> EnrichAsync(string cvText, CancellationToken cancellationToken)
    {
        using var activity = MatchingTelemetry.Source.StartActivity("enrich");
        activity?.SetTag("talentsync.prompt_version", inner.PromptVersion);

        var outcome = await inner.EnrichAsync(cvText, cancellationToken);

        var outcomeName = NameOf(outcome);
        activity?.SetTag(OutcomeTag, outcomeName);
        if (outcome is EnrichmentOutcome.Failed)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        Calls.Add(1, new KeyValuePair<string, object?>(OutcomeTag, outcomeName));
        return outcome;
    }

    private static string NameOf(EnrichmentOutcome outcome) => outcome switch
    {
        EnrichmentOutcome.Enriched => "enriched",
        EnrichmentOutcome.Failed { Reason: EnrichmentFailure.Timeout } => "timeout",
        EnrichmentOutcome.Failed { Reason: EnrichmentFailure.Unavailable } => "unavailable",
        EnrichmentOutcome.Failed { Reason: EnrichmentFailure.InvalidOutput } => "invalid_output",
        _ => throw new InvalidOperationException("Unknown enrichment outcome."),
    };
}
