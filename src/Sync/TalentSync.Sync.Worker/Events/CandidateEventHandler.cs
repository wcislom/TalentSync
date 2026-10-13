using TalentSync.Contracts;
using TalentSync.Infrastructure.Messaging;
using TalentSync.Sync.Application.UseCases;

namespace TalentSync.Sync.Worker.Events;

/// <summary>
/// Translates between the consumer skeleton and the use case: <c>candidate.*</c> in, <see cref="HandlerResult"/> out.
/// The event type is not passed on: create, update and destroy all mean "sync this candidate's current state"
/// (notify-then-fetch, ADR 003).
/// </summary>
internal sealed class CandidateEventHandler(SyncCandidateToCrmUseCase syncCandidateToCrm) : IEventHandler
{
    public bool Handles(string eventType) => EventTypes.IsCandidateEvent(eventType);

    public async Task<HandlerResult> HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var result = await syncCandidateToCrm.ExecuteAsync(envelope.CandidateId, cancellationToken);

        return result switch
        {
            SyncCandidateToCrmResult.DoneResult => HandlerResult.Done,
            SyncCandidateToCrmResult.RejectedResult rejected => HandlerResult.Permanent(rejected.Reason),
            _ => throw new InvalidOperationException("Unknown sync result."),
        };
    }
}
