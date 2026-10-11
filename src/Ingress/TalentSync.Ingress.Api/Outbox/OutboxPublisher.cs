using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

namespace TalentSync.Ingress.Api.Outbox;

/// <summary>Sends one outbox row to Event Hubs with partition key <c>candidateId</c> (invariant 10).</summary>
internal sealed class OutboxPublisher(EventHubProducerClient producer)
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        producer.SendAsync(
            [new EventData(BinaryData.FromString(message.Envelope)) { ContentType = "application/json", MessageId = message.EventId }],
            new SendEventOptions { PartitionKey = message.CandidateId },
            cancellationToken);
}
