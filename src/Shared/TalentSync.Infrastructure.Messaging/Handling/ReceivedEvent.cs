namespace TalentSync.Infrastructure.Messaging.Handling;

/// <summary>
/// An event as read from one partition, independent of the Event Hubs SDK types.
/// <see cref="PartitionId"/> and <see cref="SequenceNumber"/> identify it even when its body is not an envelope.
/// </summary>
/// <param name="TraceParent">The <c>traceparent</c> property set by the relay (ADR 010); null when missing.</param>
internal sealed record ReceivedEvent(
    string PartitionId, long SequenceNumber, ReadOnlyMemory<byte> Body, string? TraceParent);
