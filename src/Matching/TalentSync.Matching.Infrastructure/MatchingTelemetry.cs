using System.Diagnostics;
using System.Diagnostics.Metrics;
using TalentSync.Matching.Application.Ports;

namespace TalentSync.Matching.Infrastructure;

/// <summary>
/// Spans and metrics of Matching's own work (ADR 010). The LLM call itself is instrumented by
/// <c>OpenTelemetryChatClient</c> under the same name. Tags hold the outcome and the prompt version: never the prompt,
/// the CV or the model's answer (invariant 14). Never throws.
/// </summary>
internal sealed class MatchingTelemetry
{
    public const string Name = "TalentSync.Matching";

    private const string OutcomeTag = "talentsync.enrichment.outcome";

    private static readonly ActivitySource Source = new(Name);

    private readonly Counter<long> _enrichments;

    public MatchingTelemetry(IMeterFactory meterFactory)
    {
        _enrichments = meterFactory.Create(Name).CreateCounter<long>(
            "talentsync.enrichment.calls", unit: "{call}", description: "Enrichment attempts by outcome.");
    }

    public Activity? StartEnrichment(string promptVersion)
    {
        var activity = Source.StartActivity("enrich");
        activity?.SetTag("talentsync.enrichment.prompt_version", promptVersion);
        return activity;
    }

    public void EndEnrichment(Activity? activity, EnrichmentOutcome outcome)
    {
        var name = NameOf(outcome);
        activity?.SetTag(OutcomeTag, name);
        if (outcome is EnrichmentOutcome.Failed)
        {
            // No error.type: a failed enrichment is an outcome, not an exception (D5).
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        _enrichments.Add(1, new KeyValuePair<string, object?>(OutcomeTag, name));
    }

    private static string NameOf(EnrichmentOutcome outcome) => outcome switch
    {
        EnrichmentOutcome.Enriched => "enriched",
        EnrichmentOutcome.Failed { Reason: EnrichmentFailure.Timeout } => "timeout",
        EnrichmentOutcome.Failed { Reason: EnrichmentFailure.Unavailable } => "unavailable",
        EnrichmentOutcome.Failed { Reason: EnrichmentFailure.InvalidOutput } => "invalid_output",
        _ => "unknown",
    };
}
