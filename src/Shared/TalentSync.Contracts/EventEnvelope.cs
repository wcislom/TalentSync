namespace TalentSync.Contracts;

/// <summary>
/// Notification published to the <c>ats-events</c> hub. Carries ids only, never attributes or PII:
/// consumers fetch the current state from the source API (notify-then-fetch).
/// </summary>
public sealed record EventEnvelope
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>SHA-256 of the raw webhook body, hex. A redelivered identical webhook has the same id.</summary>
    public required string EventId { get; init; }

    /// <summary>System the event came from. Together with <see cref="EventId"/> it is the inbox key.</summary>
    public required string Source { get; init; }

    /// <summary>Source event name, e.g. <c>job_application.update</c>. See <see cref="EventTypes"/>.</summary>
    public required string Type { get; init; }

    public required int SchemaVersion { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceId { get; init; }

    /// <summary>Partition key of every published event.</summary>
    public required string CandidateId { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }
}
