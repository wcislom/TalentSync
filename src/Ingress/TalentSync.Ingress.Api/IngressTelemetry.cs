using System.Diagnostics;
using System.Diagnostics.Metrics;
using TalentSync.Ingress.Api.Outbox;

namespace TalentSync.Ingress.Api;

/// <summary>
/// Spans and metrics of the webhook and the relay (ADR 010). Tags hold ids only, a failure only its exception type
/// (invariant 14). Never throws.
/// </summary>
internal sealed class IngressTelemetry
{
    public const string Name = "TalentSync.Ingress";

    private const string EventIdTag = "talentsync.event_id";
    private const string EventTypeTag = "talentsync.event_type";
    private const string CandidateIdTag = "talentsync.candidate_id";

    private static readonly ActivitySource Source = new(Name);

    private readonly string _hubName;
    private readonly Histogram<double> _publishDelay;

    public IngressTelemetry(IMeterFactory meterFactory, string hubName)
    {
        _hubName = hubName;

        // Milliseconds while the relay keeps up, up to hours while Event Hubs is down; the SDK's default buckets are
        // sized for milliseconds.
        _publishDelay = meterFactory.Create(Name).CreateHistogram(
            "talentsync.outbox.publish_delay",
            unit: "s",
            description: "Time from storing a webhook to publishing it.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300, 900, 3600] });
    }

    /// <summary>A child of the webhook request span, which is the root of the trace.</summary>
    public Activity? StartWebhook(string eventId, string eventName, string candidateId)
    {
        var activity = Source.StartActivity($"webhook {eventName}");
        activity?.SetTag(EventIdTag, eventId);
        activity?.SetTag(EventTypeTag, eventName);
        activity?.SetTag(CandidateIdTag, candidateId);
        return activity;
    }

    /// <summary>
    /// Continues the webhook's trace across the outbox. A row without a stored context starts a new trace: the relay
    /// loop has no ambient activity to fall back on.
    /// </summary>
    public Activity? StartSend(OutboxMessage message)
    {
        ActivityContext.TryParse(message.TraceParent, null, out var webhook);
        var activity = Source.StartActivity($"send {_hubName}", ActivityKind.Producer, webhook);
        activity?.SetTag("messaging.system", "eventhubs");
        activity?.SetTag("messaging.operation.type", "send");
        activity?.SetTag("messaging.operation.name", "send");
        activity?.SetTag("messaging.destination.name", _hubName);
        activity?.SetTag("messaging.message.id", message.EventId);
        activity?.SetTag(EventIdTag, message.EventId);
        activity?.SetTag(EventTypeTag, message.Type);
        activity?.SetTag(CandidateIdTag, message.CandidateId);
        return activity;
    }

    public void RecordPublished(OutboxMessage message)
    {
        if (message.PublishedAt is { } publishedAt)
        {
            _publishDelay.Record(
                (publishedAt - message.CreatedAt).TotalSeconds,
                new KeyValuePair<string, object?>(EventTypeTag, message.Type));
        }
    }

    /// <summary>The exception type only: its message and stack trace go to the log, which carries the trace id.</summary>
    public void Fail(Activity? activity, Exception exception)
    {
        activity?.SetTag("error.type", exception.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    /// <summary>
    /// The value to store or send as a <c>traceparent</c>. Null without a listener, and for a non-W3C id, whose length
    /// is unbounded and would fail the outbox insert.
    /// </summary>
    public string? TraceParentOf(Activity? activity) =>
        activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;
}
