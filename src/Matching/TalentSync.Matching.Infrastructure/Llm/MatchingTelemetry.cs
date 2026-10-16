using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TalentSync.Matching.Infrastructure.Llm;

/// <summary>
/// Source and meter of Matching's own spans and metrics (ADR 010), used only by the telemetry decorators.
/// Tags hold the outcome, the model and token counts: never the prompt, the CV or the model's answer (invariant 14).
/// </summary>
internal static class MatchingTelemetry
{
    public static readonly ActivitySource Source = new("TalentSync.Matching");

    public static readonly Meter Meter = new("TalentSync.Matching");
}
