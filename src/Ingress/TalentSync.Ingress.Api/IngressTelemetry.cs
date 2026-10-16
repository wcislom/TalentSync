using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TalentSync.Ingress.Api;

/// <summary>Spans and metrics of the webhook and the relay (ADR 010). Tags hold ids only (invariant 14).</summary>
internal static class IngressTelemetry
{
    public static readonly ActivitySource Source = new("TalentSync.Ingress");

    private static readonly Meter Meter = new("TalentSync.Ingress");

    /// <summary>Time a row waited in the outbox: grows while Event Hubs is down, or when the relay falls behind.</summary>
    public static readonly Histogram<double> PublishDelay = Meter.CreateHistogram<double>(
        "talentsync.outbox.publish_delay", unit: "s", description: "Time from storing a webhook to publishing it.");

    /// <summary>
    /// The value to store or send as a <c>traceparent</c>. Null without a listener, and for a non-W3C id, whose length
    /// is unbounded and would fail the outbox insert.
    /// </summary>
    public static string? TraceParentOf(Activity? activity) =>
        activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;
}
