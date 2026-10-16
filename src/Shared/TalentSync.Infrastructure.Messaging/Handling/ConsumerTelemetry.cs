using System.Diagnostics;
using System.Diagnostics.Metrics;
using TalentSync.Contracts;

namespace TalentSync.Infrastructure.Messaging.Handling;

/// <summary>
/// Spans and metrics of <see cref="EventProcessingLoop"/> (ADR 010). One span per attempt, not per event: a span is
/// exported when it ends, so one span around the whole retry loop would stay invisible for the length of an outage.
/// Tags hold ids only, a failure only its exception type (invariant 14). Never throws: telemetry must not turn a
/// handled event into a failed one.
/// </summary>
internal sealed class ConsumerTelemetry
{
    public const string Name = "TalentSync.Messaging";

    // Also the keys of the loop's log scope, so logs and spans of one event share their attribute names.
    public const string EventIdTag = "talentsync.event_id";
    public const string EventTypeTag = "talentsync.event_type";
    public const string CandidateIdTag = "talentsync.candidate_id";

    private const string ConsumerGroupTag = "messaging.consumer.group.name";
    private const string OutcomeTag = "talentsync.consumer.outcome";

    private static readonly ActivitySource Source = new(Name);

    private readonly string _hubName;
    private readonly string _consumerGroup;
    private readonly TimeProvider _timeProvider;
    private readonly Counter<long> _attempts;
    private readonly Histogram<double> _eventAge;

    public ConsumerTelemetry(IMeterFactory meterFactory, TimeProvider timeProvider, string hubName, string consumerGroup)
    {
        _hubName = hubName;
        _consumerGroup = consumerGroup;
        _timeProvider = timeProvider;

        var meter = meterFactory.Create(Name);
        _attempts = meter.CreateCounter<long>(
            "talentsync.consumer.attempts", unit: "{attempt}", description: "Processing attempts by outcome.");

        // From the webhook reaching Ingress to the effect being done: the outbox, the hub, the partition queue and the
        // handler together. Notify-then-fetch makes this the delay before the CRM or the profile shows the change.
        // Seconds normally, up to hours after an outage; the SDK's default buckets are sized for milliseconds.
        _eventAge = meter.CreateHistogram(
            "talentsync.consumer.event_age",
            unit: "s",
            description: "Time from receiving a webhook to handling its event.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300, 900, 3600] });
    }

    /// <summary>
    /// Starts the span of one attempt as a child of the relay's send span. A missing or invalid <c>traceparent</c>
    /// starts a new trace and never fails the event.
    /// </summary>
    public Attempt StartAttempt(ReceivedEvent received, EventEnvelope? envelope, int number)
    {
        ActivityContext.TryParse(received.TraceParent, null, out var send);
        var activity = Source.StartActivity($"process {_hubName}", ActivityKind.Consumer, send);
        activity?.SetTag("messaging.system", "eventhubs");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.operation.name", "process");
        activity?.SetTag("messaging.destination.name", _hubName);
        activity?.SetTag("messaging.destination.partition.id", received.PartitionId);
        activity?.SetTag(ConsumerGroupTag, _consumerGroup);
        activity?.SetTag("messaging.message.id", envelope?.EventId);
        activity?.SetTag(EventIdTag, envelope?.EventId);
        activity?.SetTag(EventTypeTag, envelope?.Type);
        activity?.SetTag(CandidateIdTag, envelope?.CandidateId);
        activity?.SetTag("talentsync.consumer.attempt", number);
        return new Attempt(this, activity, envelope);
    }

    /// <summary>
    /// Wraps the processor's checkpoint in a <c>checkpoint {hub}</c> span, a child of the attempt that writes it
    /// (semantic conventions: a settle operation). The Blob SDK's own requests are filtered out of the traces.
    /// </summary>
    public Func<CancellationToken, Task> TraceCheckpoint(Func<CancellationToken, Task> checkpoint, ReceivedEvent received) =>
        async cancellationToken =>
        {
            using var activity = Source.StartActivity($"checkpoint {_hubName}", ActivityKind.Client);
            activity?.SetTag("messaging.system", "eventhubs");
            activity?.SetTag("messaging.operation.type", "settle");
            activity?.SetTag("messaging.operation.name", "checkpoint");
            activity?.SetTag("messaging.destination.name", _hubName);
            activity?.SetTag("messaging.destination.partition.id", received.PartitionId);
            activity?.SetTag(ConsumerGroupTag, _consumerGroup);
            try
            {
                await checkpoint(cancellationToken);
            }
            catch (Exception exception)
            {
                activity?.SetTag("error.type", exception.GetType().FullName);
                activity?.SetStatus(ActivityStatusCode.Error);
                throw;
            }
        };

    private static string NameOf(AttemptOutcome outcome) => outcome switch
    {
        AttemptOutcome.Handled => "handled",
        AttemptOutcome.SkippedDuplicate => "skipped_duplicate",
        AttemptOutcome.SkippedUnhandledType => "skipped_unhandled_type",
        AttemptOutcome.Parked => "parked",
        AttemptOutcome.TransientFailure => "transient_failure",
        AttemptOutcome.UnclassifiedFailure => "unclassified_failure",
        AttemptOutcome.Cancelled => "cancelled",
        _ => "unknown",
    };

    /// <summary>One attempt. Never null, also without a listener, so the loop needs no null checks.</summary>
    internal sealed class Attempt(ConsumerTelemetry telemetry, Activity? activity, EventEnvelope? envelope) : IDisposable
    {
        public void End(AttemptOutcome outcome)
        {
            var name = NameOf(outcome);
            activity?.SetTag(OutcomeTag, name);
            telemetry._attempts.Add(
                1,
                new KeyValuePair<string, object?>(ConsumerGroupTag, telemetry._consumerGroup),
                new KeyValuePair<string, object?>(EventTypeTag, envelope?.Type),
                new KeyValuePair<string, object?>(OutcomeTag, name));

            if (outcome == AttemptOutcome.Handled && envelope is not null)
            {
                telemetry._eventAge.Record(
                    (telemetry._timeProvider.GetUtcNow() - envelope.ReceivedAt).TotalSeconds,
                    new KeyValuePair<string, object?>(ConsumerGroupTag, telemetry._consumerGroup),
                    new KeyValuePair<string, object?>(EventTypeTag, envelope.Type));
            }
        }

        /// <summary>The exception type only: its message and stack trace go to the log, which carries the trace id.</summary>
        public void Fail(Exception exception, AttemptOutcome outcome)
        {
            activity?.SetTag("error.type", exception.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error);
            End(outcome);
        }

        public void Dispose() => activity?.Dispose();
    }
}
