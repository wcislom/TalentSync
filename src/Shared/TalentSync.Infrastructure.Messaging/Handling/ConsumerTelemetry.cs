using System.Diagnostics;
using System.Diagnostics.Metrics;
using TalentSync.Contracts;

namespace TalentSync.Infrastructure.Messaging.Handling;

/// <summary>
/// Spans and metrics of <see cref="EventProcessingLoop"/> (ADR 010). One span per attempt, not per event: a span is
/// exported when it ends, so one span around the whole retry loop would stay invisible for the length of an outage.
/// Tags hold ids only (invariant 14).
/// </summary>
internal static class ConsumerTelemetry
{
    private const string ConsumerGroupTag = "messaging.consumer.group.name";
    private const string EventTypeTag = "talentsync.event_type";
    private const string OutcomeTag = "talentsync.outcome";

    private static readonly ActivitySource Source = new("TalentSync.Messaging");
    private static readonly Meter Meter = new("TalentSync.Messaging");

    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>(
        "talentsync.consumer.attempts", description: "Processing attempts by outcome.");

    // From the webhook reaching Ingress to the effect being done: the outbox, the hub, the partition queue and the
    // handler together. Notify-then-fetch makes this the delay before the CRM or the profile shows the change.
    private static readonly Histogram<double> EventAge = Meter.CreateHistogram<double>(
        "talentsync.consumer.event_age", unit: "s", description: "Time from receiving a webhook to handling its event.");

    /// <summary>
    /// Starts the span of one attempt as a child of the relay's publish span. A missing or invalid <c>traceparent</c>
    /// starts a new trace and never fails the event.
    /// </summary>
    public static Attempt StartAttempt(ReceivedEvent received, EventEnvelope? envelope, int number, TimeProvider timeProvider)
    {
        ActivityContext.TryParse(received.TraceParent, null, out var publish);
        var activity = Source.StartActivity($"process {envelope?.Type ?? "unreadable"}", ActivityKind.Consumer, publish);
        activity?.SetTag("messaging.system", "eventhubs");
        activity?.SetTag(ConsumerGroupTag, received.ConsumerGroup);
        activity?.SetTag("messaging.destination.partition.id", received.PartitionId);
        activity?.SetTag("talentsync.event_id", envelope?.EventId);
        activity?.SetTag(EventTypeTag, envelope?.Type);
        activity?.SetTag("talentsync.candidate_id", envelope?.CandidateId);
        activity?.SetTag("talentsync.attempt", number);
        return new Attempt(activity, received.ConsumerGroup, envelope, timeProvider);
    }

    private static string NameOf(AttemptOutcome outcome) => outcome switch
    {
        AttemptOutcome.Handled => "handled",
        AttemptOutcome.SkippedDuplicate => "skipped_duplicate",
        AttemptOutcome.SkippedUnhandledType => "skipped_unhandled_type",
        AttemptOutcome.Parked => "parked",
        AttemptOutcome.TransientFailure => "transient_failure",
        AttemptOutcome.UnclassifiedFailure => "unclassified_failure",
        _ => throw new InvalidOperationException($"Unknown attempt outcome {outcome}."),
    };

    /// <summary>One attempt. Never null, also without a listener, so the loop needs no null checks.</summary>
    internal sealed class Attempt(Activity? activity, string consumerGroup, EventEnvelope? envelope, TimeProvider timeProvider) : IDisposable
    {
        public void End(AttemptOutcome outcome)
        {
            var name = NameOf(outcome);
            activity?.SetTag(OutcomeTag, name);
            Attempts.Add(
                1,
                new KeyValuePair<string, object?>(ConsumerGroupTag, consumerGroup),
                new KeyValuePair<string, object?>(EventTypeTag, envelope?.Type),
                new KeyValuePair<string, object?>(OutcomeTag, name));

            if (outcome == AttemptOutcome.Handled && envelope is not null)
            {
                EventAge.Record(
                    (timeProvider.GetUtcNow() - envelope.ReceivedAt).TotalSeconds,
                    new KeyValuePair<string, object?>(ConsumerGroupTag, consumerGroup),
                    new KeyValuePair<string, object?>(EventTypeTag, envelope.Type));
            }
        }

        public void Fail(Exception exception, AttemptOutcome outcome)
        {
            activity?.AddException(exception);
            activity?.SetStatus(ActivityStatusCode.Error);
            End(outcome);
        }

        public void Dispose() => activity?.Dispose();
    }
}
