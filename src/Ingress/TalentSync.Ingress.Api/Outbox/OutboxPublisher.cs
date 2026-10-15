using System.Diagnostics;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using TalentSync.Contracts;

namespace TalentSync.Ingress.Api.Outbox;

/// <summary>Sends one outbox row to Event Hubs with partition key <c>candidateId</c> (invariant 10).</summary>
internal sealed class OutboxPublisher(EventHubProducerClient producer)
{
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // The span continues the webhook's trace across the outbox. A row without a stored context starts a new one:
        // the relay loop has no ambient activity to fall back on.
        ActivityContext.TryParse(message.TraceParent, null, out var webhook);
        using var activity = IngressTelemetry.Source.StartActivity($"publish {message.Type}", ActivityKind.Producer, webhook);
        activity?.SetTag("messaging.system", "eventhubs");
        activity?.SetTag("messaging.destination.name", producer.EventHubName);
        activity?.SetTag("talentsync.event_id", message.EventId);
        activity?.SetTag("talentsync.event_type", message.Type);
        activity?.SetTag("talentsync.candidate_id", message.CandidateId);

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
            activity?.AddException(exception);
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }
}
