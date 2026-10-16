using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using TalentSync.Contracts;
using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.Infrastructure.Messaging.ParkingLot;
using TalentSync.Infrastructure.Messaging.Persistence;

namespace TalentSync.Infrastructure.Messaging.Handling;

/// <summary>
/// Processes one event until it is handled, skipped or parked, then checkpoints. Retries stay inside this call:
/// returning without a checkpoint does not redeliver the event, and the next checkpoint would cover it, so a failed
/// event is never left behind by returning (invariant 8). Never throws (invariant 7).
/// </summary>
internal sealed class EventProcessingLoop(
    IEventHandler handler,
    IConsumerStore store,
    IOptions<ConsumerOptions> options,
    TimeProvider timeProvider,
    ILogger<EventProcessingLoop> logger)
{
    internal const string UnreadableEnvelope = "unreadable envelope";
    internal const int MaxParkedBodyBytes = 4096;

    private const string OutcomeTag = "talentsync.outcome";

    private static readonly ActivitySource ActivitySource = new("TalentSync.Messaging");
    private static readonly Meter Meter = new("TalentSync.Messaging");

    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>(
        "talentsync.consumer.attempts", description: "Processing attempts by outcome.");

    // From the webhook reaching Ingress to the effect being done: the outbox, the hub, the partition queue and the
    // handler together. Notify-then-fetch makes this the delay before the CRM or the profile shows the change.
    private static readonly Histogram<double> EventAge = Meter.CreateHistogram<double>(
        "talentsync.consumer.event_age", unit: "s", description: "Time from receiving a webhook to handling its event.");

    private readonly ConsumerOptions _options = options.Value;

    // Every failure but a shutdown is retried: transient ones for as long as the outage lasts, unclassified ones until
    // the callback parks the event. int.MaxValue attempts at the 30 s cap is far beyond any outage, so the loop never
    // gives up in practice.
    private readonly ResiliencePipeline _retry = new ResiliencePipelineBuilder { TimeProvider = timeProvider }
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is { } exception && !IsShutdown(exception, args.Context.CancellationToken)),
            MaxRetryAttempts = int.MaxValue,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = false,
            Delay = options.Value.InitialBackoff,
            MaxDelay = options.Value.MaxBackoff,
        })
        .Build();

    /// <param name="checkpoint">Marks this event and every earlier one in the partition as done.</param>
    /// <param name="cancellationToken">Cancelled when the processor shuts down or loses the partition.</param>
    public async Task ProcessAsync(ReceivedEvent received, Func<CancellationToken, Task> checkpoint, CancellationToken cancellationToken)
    {
        var envelope = TryReadEnvelope(received.Body.Span);
        var failedAttempts = 0;
        var unclassifiedAttempts = 0;

        // Every log of the use case and the clients carries these, without passing them around.
        using var logScope = envelope is null ? null : logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = envelope.EventId,
            ["EventType"] = envelope.Type,
            ["CandidateId"] = envelope.CandidateId,
        });

        try
        {
            await _retry.ExecuteAsync(async token =>
            {
                // A span per attempt, not per event: a span is exported when it ends, so one span around the whole
                // retry loop would stay invisible for the length of an outage (ADR 010).
                using var activity = StartAttempt(received, envelope, failedAttempts + 1);
                try
                {
                    var outcome = await HandleOnceAsync(received, envelope, checkpoint, token);
                    EndAttempt(activity, received, envelope, outcome);
                }
                catch (Exception exception) when (!IsShutdown(exception, token))
                {
                    failedAttempts++;
                    activity?.AddException(exception);
                    activity?.SetStatus(ActivityStatusCode.Error);
                    if (FailureClassifier.IsTransient(exception))
                    {
                        EndAttempt(activity, received, envelope, Outcomes.TransientFailure);
                        logger.LogWarning(exception, "Transient failure on {EventId} (partition {PartitionId}, sequence {SequenceNumber}), attempt {Attempt}",
                            envelope?.EventId, received.PartitionId, received.SequenceNumber, failedAttempts);
                        throw;
                    }

                    unclassifiedAttempts++;
                    logger.LogError(exception, "Unclassified failure on {EventId} (partition {PartitionId}, sequence {SequenceNumber}), attempt {Attempt} of {MaxAttempts}",
                        envelope?.EventId, received.PartitionId, received.SequenceNumber, unclassifiedAttempts, _options.MaxUnclassifiedAttempts);

                    if (unclassifiedAttempts >= _options.MaxUnclassifiedAttempts
                        && await TryParkAsync(received, envelope, $"retry limit exceeded: {exception.GetType().FullName}", checkpoint, token))
                    {
                        EndAttempt(activity, received, envelope, Outcomes.Parked);
                        return;
                    }

                    EndAttempt(activity, received, envelope, Outcomes.UnclassifiedFailure);
                    throw;
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, during an attempt or a back-off: no checkpoint, so the next owner of the partition processes
            // the event again.
        }
    }

    private static bool IsShutdown(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    /// <summary>A child of the relay's publish span; a missing or invalid <c>traceparent</c> starts a new trace.</summary>
    private static Activity? StartAttempt(ReceivedEvent received, EventEnvelope? envelope, int attempt)
    {
        ActivityContext.TryParse(received.TraceParent, null, out var publish);
        var activity = ActivitySource.StartActivity($"process {envelope?.Type ?? "unreadable"}", ActivityKind.Consumer, publish);
        activity?.SetTag("messaging.system", "eventhubs");
        activity?.SetTag("messaging.consumer.group.name", received.ConsumerGroup);
        activity?.SetTag("messaging.destination.partition.id", received.PartitionId);
        activity?.SetTag("talentsync.event_id", envelope?.EventId);
        activity?.SetTag("talentsync.event_type", envelope?.Type);
        activity?.SetTag("talentsync.candidate_id", envelope?.CandidateId);
        activity?.SetTag("talentsync.attempt", attempt);
        return activity;
    }

    private void EndAttempt(Activity? activity, ReceivedEvent received, EventEnvelope? envelope, string outcome)
    {
        activity?.SetTag(OutcomeTag, outcome);
        Attempts.Add(
            1,
            new KeyValuePair<string, object?>("messaging.consumer.group.name", received.ConsumerGroup),
            new KeyValuePair<string, object?>("talentsync.event_type", envelope?.Type),
            new KeyValuePair<string, object?>(OutcomeTag, outcome));

        if (outcome == Outcomes.Handled && envelope is not null)
        {
            EventAge.Record(
                (timeProvider.GetUtcNow() - envelope.ReceivedAt).TotalSeconds,
                new KeyValuePair<string, object?>("messaging.consumer.group.name", received.ConsumerGroup),
                new KeyValuePair<string, object?>("talentsync.event_type", envelope.Type));
        }
    }

    /// <returns>The outcome of the attempt, one of <see cref="Outcomes"/>.</returns>
    private async Task<string> HandleOnceAsync(
        ReceivedEvent received, EventEnvelope? envelope, Func<CancellationToken, Task> checkpoint, CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            await store.ParkAsync(NewParkedEvent(received, envelope: null, UnreadableEnvelope), cancellationToken);
            await checkpoint(cancellationToken);
            logger.LogWarning("Parked unreadable event (partition {PartitionId}, sequence {SequenceNumber})", received.PartitionId, received.SequenceNumber);
            return Outcomes.Parked;
        }

        if (!handler.Handles(envelope.Type))
        {
            await checkpoint(cancellationToken);
            return Outcomes.SkippedUnhandledType;
        }

        if (await store.IsHandledAsync(envelope.Source, envelope.EventId, cancellationToken))
        {
            await checkpoint(cancellationToken);
            return Outcomes.SkippedDuplicate;
        }

        var result = await handler.HandleAsync(envelope, cancellationToken);

        switch (result)
        {
            case HandlerResult.PermanentResult permanent:
                await store.ParkAsync(NewParkedEvent(received, envelope, permanent.Reason), cancellationToken);
                await checkpoint(cancellationToken);
                logger.LogWarning("Parked {EventId} ({EventType}) for candidate {CandidateId}: {Reason}",
                    envelope.EventId, envelope.Type, envelope.CandidateId, permanent.Reason);
                return Outcomes.Parked;

            case HandlerResult.DoneResult:
                // After the effect, never before (invariant 3). A crash here repeats the effect on redelivery,
                // which the idempotent effect makes harmless (invariant 2).
                await store.MarkHandledAsync(
                    new InboxEntry { Source = envelope.Source, EventId = envelope.EventId, ProcessedAt = UtcNow() },
                    cancellationToken);
                await checkpoint(cancellationToken);
                logger.LogInformation("Handled {EventId} ({EventType}) for candidate {CandidateId}", envelope.EventId, envelope.Type, envelope.CandidateId);
                return Outcomes.Handled;

            default:
                throw new InvalidOperationException($"Unknown handler result {result.GetType().Name}.");
        }
    }

    /// <returns>False when parking or the checkpoint failed: the loop goes on, the event is never checkpointed unparked.</returns>
    private async Task<bool> TryParkAsync(
        ReceivedEvent received, EventEnvelope? envelope, string error, Func<CancellationToken, Task> checkpoint, CancellationToken cancellationToken)
    {
        try
        {
            await store.ParkAsync(NewParkedEvent(received, envelope, error), cancellationToken);
            await checkpoint(cancellationToken);
            logger.LogWarning("Parked {EventId} (partition {PartitionId}, sequence {SequenceNumber}): {Error}",
                envelope?.EventId, received.PartitionId, received.SequenceNumber, error);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Could not park {EventId} (partition {PartitionId}, sequence {SequenceNumber})",
                envelope?.EventId, received.PartitionId, received.SequenceNumber);
            return false;
        }
    }

    private static EventEnvelope? TryReadEnvelope(ReadOnlySpan<byte> body) =>
        EventEnvelopeJson.TryDeserialize(body, out var envelope) ? envelope : null;

    private ParkedEvent NewParkedEvent(ReceivedEvent received, EventEnvelope? envelope, string error) => new()
    {
        Source = envelope?.Source,
        EventId = envelope?.EventId,
        PartitionId = received.PartitionId,
        SequenceNumber = received.SequenceNumber,
        Envelope = envelope is not null
            ? EventEnvelopeJson.Serialize(envelope)
            : Encoding.UTF8.GetString(received.Body.Span[..Math.Min(received.Body.Length, MaxParkedBodyBytes)]),
        Error = error,
        ParkedAt = UtcNow(),
    };

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Values of the <c>talentsync.outcome</c> tag of an attempt span.</summary>
    private static class Outcomes
    {
        public const string Handled = "handled";
        public const string SkippedDuplicate = "skipped_duplicate";
        public const string SkippedUnhandledType = "skipped_unhandled_type";
        public const string Parked = "parked";
        public const string TransientFailure = "transient_failure";
        public const string UnclassifiedFailure = "unclassified_failure";
    }
}
