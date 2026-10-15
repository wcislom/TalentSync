using System.Diagnostics;

namespace TalentSync.Ingress.Api;

/// <summary>Spans of the webhook and the relay (ADR 010). Tags hold ids only (invariant 14).</summary>
internal static class IngressTelemetry
{
    public static readonly ActivitySource Source = new("TalentSync.Ingress");

    /// <summary>
    /// The value to store or send as a <c>traceparent</c>. Null without a listener, and for a non-W3C id, whose length
    /// is unbounded and would fail the outbox insert.
    /// </summary>
    public static string? TraceParentOf(Activity? activity) =>
        activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;
}
