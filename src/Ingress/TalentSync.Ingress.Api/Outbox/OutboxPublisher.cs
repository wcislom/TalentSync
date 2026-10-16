using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using TalentSync.Contracts;

namespace TalentSync.Ingress.Api.Outbox;

/// <summary>Sends one outbox row to Event Hubs with partition key <c>candidateId</c> (invariant 10).</summary>
internal sealed class OutboxPublisher(EventHubProducerClient producer)
{
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        using var activity = IngressTelemetry.StartPublish(message, producer.EventHubName);

        var eventData = new EventData(BinaryData.FromString(message.Envelope)) { ContentType = "application/json", MessageId = message.EventId };
        if (IngressTelemetry.TraceParentOf(activity) is { } traceParent)
        {
            eventData.Properties[EventProperties.TraceParent] = traceParent;
        }

        try
        {
            await producer.SendAsync([eventData], new SendEventOptions { PartitionKey = message.CandidateId }, cancellationToken);
        }
        catch (Exception exception)
        {
            IngressTelemetry.Fail(activity, exception);
            throw;
        }
    }
}
