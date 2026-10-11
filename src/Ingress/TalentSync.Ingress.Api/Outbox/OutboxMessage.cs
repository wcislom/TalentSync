namespace TalentSync.Ingress.Api.Outbox;

/// <summary>
/// An envelope waiting to be published. Rows are never deduplicated by <see cref="EventId"/>: a redelivered webhook
/// is stored again and consumers skip it through their inbox (at-least-once, invariant 1).
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Clustered key; the relay publishes in this order.</summary>
    public long Id { get; private set; }

    public required string EventId { get; init; }

    /// <summary>Partition key used by the relay.</summary>
    public required string CandidateId { get; init; }

    public required string Type { get; init; }

    /// <summary>Serialized <c>EventEnvelope</c>: ids only, no PII (invariant 14).</summary>
    public required string Envelope { get; init; }

    /// <summary>UTC, from <see cref="TimeProvider"/>.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC. Null until the relay has published the row.</summary>
    public DateTime? PublishedAt { get; set; }
}
