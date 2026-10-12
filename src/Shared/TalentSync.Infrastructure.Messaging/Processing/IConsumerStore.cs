using TalentSync.Infrastructure.Messaging.Inbox;
using TalentSync.Infrastructure.Messaging.ParkingLot;

namespace TalentSync.Infrastructure.Messaging.Processing;

/// <summary>The worker's <c>Inbox</c> and <c>ParkingLot</c> tables, in its own schema.</summary>
internal interface IConsumerStore
{
    /// <summary>Skip-optimisation only (invariant 3): a false answer is never a reason to skip the effect.</summary>
    Task<bool> IsHandledAsync(string source, string eventId, CancellationToken cancellationToken);

    /// <summary>Succeeds when the row already exists: that means another attempt or processor got there first.</summary>
    Task MarkHandledAsync(InboxEntry entry, CancellationToken cancellationToken);

    Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken);
}
