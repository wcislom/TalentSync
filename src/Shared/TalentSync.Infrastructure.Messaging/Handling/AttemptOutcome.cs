namespace TalentSync.Infrastructure.Messaging.Handling;

/// <summary>How one attempt of <see cref="EventProcessingLoop"/> at one event ended.</summary>
internal enum AttemptOutcome
{
    /// <summary>The effect is done and the inbox row written.</summary>
    Handled,

    /// <summary>The event is already in the inbox: a redelivery (invariant 1).</summary>
    SkippedDuplicate,

    /// <summary>The worker does not handle this event type.</summary>
    SkippedUnhandledType,

    Parked,

    /// <summary>Retried for as long as the outage lasts (invariant 8).</summary>
    TransientFailure,

    /// <summary>Retried until the attempt limit, then parked.</summary>
    UnclassifiedFailure,
}
