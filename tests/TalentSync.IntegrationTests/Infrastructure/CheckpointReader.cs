using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>
/// Reads a consumer group's checkpoints straight from the blob container, to know how far a worker has processed a
/// partition. Relies on the Event Hubs SDK's blob layout: one blob per partition named
/// <c>{namespace}/{hub}/{consumer group}/checkpoint/{partition id}</c>, the position in metadata <c>sequencenumber</c>.
/// </summary>
public sealed class CheckpointReader(BlobContainerClient container, string consumerGroup)
{
    /// <returns>Null while the partition has no checkpoint yet.</returns>
    public async Task<long?> GetSequenceNumberAsync(string partitionId)
    {
        var suffix = $"/{consumerGroup}/checkpoint/{partitionId}";
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.Metadata, cancellationToken: TestContext.Current.CancellationToken))
        {
            if (blob.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && blob.Metadata.TryGetValue("sequencenumber", out var value))
            {
                return long.Parse(value);
            }
        }

        return null;
    }
}
