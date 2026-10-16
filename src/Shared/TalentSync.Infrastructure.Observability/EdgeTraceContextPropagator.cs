using System.Diagnostics;

namespace TalentSync.Infrastructure.Observability;

/// <summary>
/// Ignores the trace context of incoming HTTP requests, so every request to our hosts starts a new trace (ADR 010).
/// Our hosts are the edge: their only callers are webhook senders and probes, whose context is untrusted, and one
/// sender trace can carry several webhooks. Outgoing requests keep the default W3C injection.
/// </summary>
internal sealed class EdgeTraceContextPropagator : DistributedContextPropagator
{
    private static readonly DistributedContextPropagator Outgoing = CreateDefaultPropagator();

    public override IReadOnlyCollection<string> Fields => Outgoing.Fields;

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter) =>
        Outgoing.Inject(activity, carrier, setter);

    public override void ExtractTraceIdAndState(
        object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState)
    {
        traceId = null;
        traceState = null;
    }

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) => null;
}
