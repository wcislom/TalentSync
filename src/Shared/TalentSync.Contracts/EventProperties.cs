namespace TalentSync.Contracts;

/// <summary>
/// Application properties of a published event: transport metadata, kept out of <see cref="EventEnvelope"/>,
/// which is the business contract. Consumers ignore properties they do not know (invariant 11).
/// </summary>
public static class EventProperties
{
    /// <summary>W3C trace context of the publish span (ADR 010). Missing or invalid starts a new trace.</summary>
    public const string TraceParent = "traceparent";
}
