using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TalentSync.Contracts;
using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.Infrastructure.Messaging.ParkingLot;

namespace TalentSync.Infrastructure.Messaging.Processing;

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

    /// <param name="checkpoint">Marks this event and every earlier one in the partition as done.</param>
    /// <param name="cancellationToken">Cancelled when the processor shuts down or loses the partition.</param>
    public async Task ProcessAsync(ReceivedEvent received, Func<CancellationToken, Task> checkpoint, CancellationToken cancellationToken)
    {
        var envelope = TryReadEnvelope(received.Body.Span);
        var failedAttempts = 0;
        var unclassifiedAttempts = 0;

        while (true)
        {
            try
            {
                await HandleOnceAsync(received, envelope, checkpoint, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown: no checkpoint, so the next owner of the partition processes the event again.
                return;
            }
            catch (Exception exception) when (FailureClassifier.IsTransient(exception))
            {
                failedAttempts++;
                logger.LogWarning(exception, "Transient failure on {EventId} (partition {PartitionId}, sequence {SequenceNumber}), attempt {Attempt}",
                    envelope?.EventId, received.PartitionId, received.SequenceNumber, failedAttempts);
            }
            catch (Exception exception)
            {
                failedAttempts++;
                unclassifiedAttempts++;
                logger.LogError(exception, "Unclassified failure on {EventId} (partition {PartitionId}, sequence {SequenceNumber}), attempt {Attempt} of {MaxAttempts}",
                    envelope?.EventId, received.PartitionId, received.SequenceNumber, unclassifiedAttempts, _options.MaxUnclassifiedAttempts);

                if (unclassifiedAttempts >= _options.MaxUnclassifiedAttempts
                    && await TryParkAsync(received, envelope, $"retry limit exceeded: {exception.GetType().FullName}", checkpoint, cancellationToken))
                {
                    return;
                }
            }

            try
            {
                await Task.Delay(Backoff(failedAttempts), timeProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

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

    private TimeSpan Backoff(int failedAttempts)
    {
        var delay = _options.InitialBackoff * Math.Pow(2, Math.Min(failedAttempts - 1, 16));
        return delay < _options.MaxBackoff ? delay : _options.MaxBackoff;
    }
}
