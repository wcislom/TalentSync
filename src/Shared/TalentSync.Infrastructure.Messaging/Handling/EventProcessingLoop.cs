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

        try
        {
            await _retry.ExecuteAsync(async token =>
            {
                try
                {
                    await HandleOnceAsync(received, envelope, checkpoint, token);
                }
                catch (Exception exception) when (!IsShutdown(exception, token))
                {
                    failedAttempts++;
                    if (FailureClassifier.IsTransient(exception))
                    {
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
                        return;
                    }

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

    private async Task HandleOnceAsync(
        ReceivedEvent received, EventEnvelope? envelope, Func<CancellationToken, Task> checkpoint, CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            await store.ParkAsync(NewParkedEvent(received, envelope: null, UnreadableEnvelope), cancellationToken);
            await checkpoint(cancellationToken);
            logger.LogWarning("Parked unreadable event (partition {PartitionId}, sequence {SequenceNumber})", received.PartitionId, received.SequenceNumber);
            return;
        }

        if (!handler.Handles(envelope.Type)
            || await store.IsHandledAsync(envelope.Source, envelope.EventId, cancellationToken))
        {
            await checkpoint(cancellationToken);
            return;
        }

        var result = await handler.HandleAsync(envelope, cancellationToken);

        switch (result)
        {
            case HandlerResult.PermanentResult permanent:
                await store.ParkAsync(NewParkedEvent(received, envelope, permanent.Reason), cancellationToken);
                await checkpoint(cancellationToken);
                logger.LogWarning("Parked {EventId} ({EventType}) for candidate {CandidateId}: {Reason}",
                    envelope.EventId, envelope.Type, envelope.CandidateId, permanent.Reason);
                return;

            case HandlerResult.DoneResult:
                // After the effect, never before (invariant 3). A crash here repeats the effect on redelivery,
                // which the idempotent effect makes harmless (invariant 2).
                await store.MarkHandledAsync(
                    new InboxEntry { Source = envelope.Source, EventId = envelope.EventId, ProcessedAt = UtcNow() },
                    cancellationToken);
                await checkpoint(cancellationToken);
                logger.LogInformation("Handled {EventId} ({EventType}) for candidate {CandidateId}", envelope.EventId, envelope.Type, envelope.CandidateId);
                return;

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
}
