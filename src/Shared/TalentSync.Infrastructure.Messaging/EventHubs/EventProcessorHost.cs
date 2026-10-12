using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TalentSync.Infrastructure.Messaging.Handling;

namespace TalentSync.Infrastructure.Messaging.EventHubs;

/// <summary>
/// Starts and stops the worker's <see cref="EventProcessorClient"/> and hands each event to
/// <see cref="EventProcessingLoop"/>. The SDK calls the event handler for one partition at a time, in order, so
/// events of one candidate (one partition key) are never processed concurrently.
/// </summary>
internal sealed class EventProcessorHost(
    EventProcessorClient processor,
    BlobContainerClient checkpointContainer,
    IServiceScopeFactory scopeFactory,
    ILogger<EventProcessorHost> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The checkpoint store does not create its container. Without it every checkpoint and ownership claim fails.
        await checkpointContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        processor.ProcessEventAsync += OnProcessEventAsync;
        processor.ProcessErrorAsync += OnProcessErrorAsync;
        await processor.StartProcessingAsync(cancellationToken);
        logger.LogInformation("Consuming {EventHubName} as consumer group {ConsumerGroup}", processor.EventHubName, processor.ConsumerGroup);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancels the token of every running handler: the loop returns without a checkpoint, and the event is
        // processed again by the next owner of the partition.
        await processor.StopProcessingAsync(cancellationToken);
        processor.ProcessEventAsync -= OnProcessEventAsync;
        processor.ProcessErrorAsync -= OnProcessErrorAsync;
    }

    private async Task OnProcessEventAsync(ProcessEventArgs args)
    {
        if (!args.HasEvent)
        {
            return;
        }

        // A scope per event, so the worker's handler can use scoped and transient services (typed HttpClients).
        await using var scope = scopeFactory.CreateAsyncScope();
        var loop = scope.ServiceProvider.GetRequiredService<EventProcessingLoop>();

        var received = new ReceivedEvent(args.Partition.PartitionId, args.Data.SequenceNumber, args.Data.EventBody.ToMemory());
        await loop.ProcessAsync(received, args.UpdateCheckpointAsync, args.CancellationToken);
    }

    // Failures of the processor itself (connection, load balancing, ownership), not of an event. The processor
    // recovers on its own; there is nothing to retry here.
    private Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogWarning(args.Exception, "Event processor error in {Operation} (partition {PartitionId})", args.Operation, args.PartitionId);
        return Task.CompletedTask;
    }
}
