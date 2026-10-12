namespace TalentSync.Infrastructure.Messaging.Inbox;

/// <summary>
/// An event this worker has handled. Written after the effect, never before (invariant 3): it only lets a duplicate
/// skip the fetch and the effect; correctness comes from the idempotent effect (invariant 2).
/// </summary>
public sealed class InboxEntry
{
    public long Id { get; private set; }

    /// <summary>Together with <see cref="EventId"/> the unique key; a duplicate insert means "already handled".</summary>
    public required string Source { get; init; }

    public required string EventId { get; init; }

    /// <summary>UTC, from <see cref="TimeProvider"/>.</summary>
    public required DateTime ProcessedAt { get; init; }
}
