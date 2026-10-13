using Azure.Messaging.EventHubs.Consumer;
using TalentSync.Contracts;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>An event read back from the hub: the envelope, the partition key it was published with, and where it landed.</summary>
public sealed record HubEvent(EventEnvelope Envelope, string? PartitionKey, string PartitionId, long SequenceNumber);

/// <summary>
/// Reads the hub from the beginning through the <c>$Default</c> consumer group, which no worker uses.
/// The hub is small in tests, so each call reads every partition to its end.
/// </summary>
public sealed class HubReader(string connectionString, string hubName)
{
    private static readonly ReadEventOptions ReadToEnd = new() { MaximumWaitTime = TimeSpan.FromSeconds(1) };

    public async Task<IReadOnlyList<HubEvent>> ReadByEventIdAsync(string eventId)
    {
        await using var consumer = new EventHubConsumerClient(EventHubConsumerClient.DefaultConsumerGroupName, connectionString, hubName);

        var found = new List<HubEvent>();
        foreach (var partitionId in await consumer.GetPartitionIdsAsync())
        {
            await foreach (var partitionEvent in consumer.ReadEventsFromPartitionAsync(partitionId, EventPosition.Earliest, ReadToEnd))
            {
                // MaximumWaitTime elapsed with no event: the partition is read to its end.
                if (partitionEvent.Data is null)
                {
                    break;
                }

                if (EventEnvelopeJson.TryDeserialize(partitionEvent.Data.EventBody.ToMemory().Span, out var envelope)
                    && envelope.EventId == eventId)
                {
                    found.Add(new HubEvent(envelope, partitionEvent.Data.PartitionKey, partitionId, partitionEvent.Data.SequenceNumber));
                }
            }
        }

        return found;
    }
}
