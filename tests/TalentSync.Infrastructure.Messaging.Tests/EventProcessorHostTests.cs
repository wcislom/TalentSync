using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TalentSync.Infrastructure.Messaging.EventHubs;

namespace TalentSync.Infrastructure.Messaging.Tests;

public class EventProcessorHostTests
{
    // Nothing is contacted: the clients connect lazily and the processor is never started.
    private const string EventHubsConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;UseDevelopmentEmulator=true";

    [Fact]
    public async Task StopAsync_CalledTwiceConcurrently_DoesNotThrow()
    {
        var checkpoints = new BlobContainerClient("UseDevelopmentStorage=true", "checkpoints");
        var processor = new EventProcessorClient(checkpoints, "test-group", EventHubsConnectionString, "test-hub");
        var host = new EventProcessorHost(
            processor,
            checkpoints,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventProcessorHost>.Instance);

        var token = TestContext.Current.CancellationToken;
        await Task.WhenAll(
            Task.Run(() => host.StopAsync(token), token),
            Task.Run(() => host.StopAsync(token), token));
    }
}
