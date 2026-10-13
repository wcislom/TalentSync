using TalentSync.Contracts;
using TalentSync.Infrastructure.Messaging;
using TalentSync.Matching.Application.UseCases;

namespace TalentSync.Matching.Worker.Events;

/// <summary>
/// Translates between the consumer skeleton and the use case: <c>job_application.*</c> in, <see cref="HandlerResult"/>
/// out. <c>candidate.*</c> is not handled in the MVP (D6): the skeleton checkpoints it without an inbox row.
/// </summary>
internal sealed class JobApplicationEventHandler(CreateConsultantProfileUseCase createConsultantProfile) : IEventHandler
{
    public bool Handles(string eventType) => EventTypes.IsJobApplicationEvent(eventType);

    public async Task<HandlerResult> HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var result = await createConsultantProfile.ExecuteAsync(envelope.ResourceId, cancellationToken);

        return result switch
        {
            CreateConsultantProfileResult.UpsertedResult or CreateConsultantProfileResult.SkippedResult => HandlerResult.Done,
            CreateConsultantProfileResult.RejectedResult rejected => HandlerResult.Permanent(rejected.Reason),
            _ => throw new InvalidOperationException("Unknown create profile result."),
        };
    }
}
