namespace TalentSync.Infrastructure.Messaging.ParkingLot;

/// <summary>
/// An event this worker gave up on and checkpointed past: a permanent failure, an unreadable envelope, or the retry
/// limit of an unclassified exception. Not deduplicated: a crash between the insert and the checkpoint parks the
/// event twice (accepted in the MVP).
/// </summary>
public sealed class ParkedEvent
{
    public long Id { get; private set; }

    /// <summary>Null when the envelope could not be read.</summary>
    public string? Source { get; init; }

    /// <summary>Null when the envelope could not be read.</summary>
    public string? EventId { get; init; }

    /// <summary>With <see cref="SequenceNumber"/> identifies the event in the hub, also when the envelope is unreadable.</summary>
    public required string PartitionId { get; init; }

    public required long SequenceNumber { get; init; }

    /// <summary>The serialized envelope, or the raw event body truncated to 4 KB when it could not be read.</summary>
    public required string Envelope { get; init; }

    /// <summary>Exception type and message, or the rejection reason. Never PII or response bodies (invariant 14).</summary>
    public required string Error { get; init; }

    /// <summary>UTC, from <see cref="TimeProvider"/>.</summary>
    public required DateTime ParkedAt { get; init; }
}
