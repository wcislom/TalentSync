using TalentSync.Contracts;

namespace TalentSync.Infrastructure.Messaging.Consumer;

/// <summary>
/// A worker's part of event processing: runs its use case and translates the outcome. The inbox, parking lot,
/// retries and checkpoints are the skeleton's job.
/// </summary>
public interface IEventHandler
{
    /// <summary>Event types this worker acts on. Any other type is checkpointed without an inbox row.</summary>
    bool Handles(string eventType);

    /// <summary>
    /// Returns <see cref="HandlerResult.Done"/> or <see cref="HandlerResult.Permanent"/>. Throws for transient
    /// failures; never catches them. May run more than once for the same event, so the effect must be idempotent.
    /// </summary>
    Task<HandlerResult> HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}
