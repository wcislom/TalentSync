using System.Diagnostics;
using System.Diagnostics.Metrics;
using TalentSync.Ingress.Api.Outbox;

namespace TalentSync.Ingress.Api;

/// <summary>Spans and metrics of the webhook and the relay (ADR 010). Tags hold ids only (invariant 14).</summary>
internal static class IngressTelemetry
{
    private static readonly ActivitySource Source = new("TalentSync.Ingress");
    private static readonly Meter Meter = new("TalentSync.Ingress");

    // Grows while Event Hubs is down, or when the relay falls behind.
    private static readonly Histogram<double> PublishDelay = Meter.CreateHistogram<double>(
        "talentsync.outbox.publish_delay", unit: "s", description: "Time from storing a webhook to publishing it.");

    /// <summary>A child of the webhook request span, which is the root of the trace.</summary>
    public static Activity? StartWebhook(string eventId, string eventName, string candidateId)
    {
        var activity = Source.StartActivity($"webhook {eventName}");
        activity?.SetTag("talentsync.event_id", eventId);
        activity?.SetTag("talentsync.event_type", eventName);
        activity?.SetTag("talentsync.candidate_id", candidateId);
        return activity;
    }

    /// <summary>
    /// Continues the webhook's trace across the outbox. A row without a stored context starts a new trace: the relay
    /// loop has no ambient activity to fall back on.
    /// </summary>
    public static Activity? StartPublish(OutboxMessage message, string hubName)
    {
        ActivityContext.TryParse(message.TraceParent, null, out var webhook);
        var activity = Source.StartActivity($"publish {message.Type}", ActivityKind.Producer, webhook);
        activity?.SetTag("messaging.system", "eventhubs");
        activity?.SetTag("messaging.destination.name", hubName);
        activity?.SetTag("talentsync.event_id", message.EventId);
        activity?.SetTag("talentsync.event_type", message.Type);
        activity?.SetTag("talentsync.candidate_id", message.CandidateId);
        return activity;
    }

    public static void RecordPublished(OutboxMessage message)
    {
        if (message.PublishedAt is { } publishedAt)
        {
            PublishDelay.Record(
                (publishedAt - message.CreatedAt).TotalSeconds,
                new KeyValuePair<string, object?>("talentsync.event_type", message.Type));
        }
    }

    public static void Fail(Activity? activity, Exception exception)
    {
        activity?.AddException(exception);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    /// <summary>
    /// The value to store or send as a <c>traceparent</c>. Null without a listener, and for a non-W3C id, whose length
    /// is unbounded and would fail the outbox insert.
    /// </summary>
    public static string? TraceParentOf(Activity? activity) =>
        activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;
}
