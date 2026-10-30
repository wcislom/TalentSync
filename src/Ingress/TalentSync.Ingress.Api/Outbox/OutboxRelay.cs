using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using TalentSync.Ingress.Api.Persistence;

namespace TalentSync.Ingress.Api.Outbox;

/// <summary>
/// Publishes unpublished outbox rows in <see cref="OutboxMessage.Id"/> order and marks them published: the second
/// half of the transactional outbox (ADR 004). Exactly one replica runs (invariant 13).
/// </summary>
/// <remarks>
/// Delivery is at-least-once: a crash, or a failed commit, after a send and before the row is marked publishes the
/// event again. So does a send that times out after the event reached the hub. Consumers skip the duplicate through
/// their inbox (same <c>EventId</c>, invariant 1).
/// </remarks>
internal sealed class OutboxRelay(
    IDbContextFactory<IngressDbContext> dbContextFactory,
    OutboxPublisher publisher,
    IngressTelemetry telemetry,
    IOptions<OutboxRelayOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    private readonly OutboxRelayOptions _options = options.Value;

    // Never let an exception escape: by default it stops the whole host, webhook endpoint included. While SQL or
    // Event Hubs is unavailable the rows stay unpublished, webhooks keep getting 200, and the batch is retried for as
    // long as the outage lasts (int.MaxValue attempts at the back-off cap is beyond any outage).
    private readonly ResiliencePipeline _retry = new ResiliencePipelineBuilder { TimeProvider = timeProvider }
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is { } exception
                && !(exception is OperationCanceledException && args.Context.CancellationToken.IsCancellationRequested)),
            MaxRetryAttempts = int.MaxValue,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = false,
            Delay = options.Value.PollInterval,
            MaxDelay = options.Value.MaxBackoff,
            OnRetry = args =>
            {
                logger.LogWarning(args.Outcome.Exception, "Outbox relay batch failed (attempt {Attempt}), retrying in {Delay}",
                    args.AttemptNumber + 1, args.RetryDelay);
                return ValueTask.CompletedTask;
            },
        })
        .Build();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Seen in the log of an API replica, it means OutboxRelay:Enabled is missing there (invariant 13).
        logger.LogInformation("Outbox relay started");

        try
        {
            while (true)
            {
                var fetched = await _retry.ExecuteAsync(async token => await RelayBatchAsync(token), stoppingToken);

                // A full batch means more rows may be waiting: publish them without waiting.
                if (fetched < _options.BatchSize)
                {
                    await Task.Delay(_options.PollInterval, timeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <returns>The number of rows fetched.</returns>
    private async Task<int> RelayBatchAsync(CancellationToken stoppingToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(stoppingToken);
        await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
        var rows = await db.ClaimUnpublishedAsync(_options.BatchSize, stoppingToken);

        // One send per row, on purpose. A send of several events takes a single partition key, so batching could only
        // group rows of the same candidate, and a batch rarely holds two events of one candidate: the gain is near
        // zero, while grouping adds the 1 MB send limit (EventDataBatch) and marking per group. The producer reuses
        // one AMQP connection, so a send costs a round trip, which recruitment webhook volume easily affords.
        //
        // The first failed send ends the batch: sending the rows after it could publish a later event of a candidate
        // before an earlier one. Rows sent so far are still marked, so they are not published twice.
        ExceptionDispatchInfo? sendFailure = null;
        var published = 0;
        foreach (var row in rows)
        {
            try
            {
                await publisher.PublishAsync(row, stoppingToken);
            }
            catch (Exception exception)
            {
                sendFailure = ExceptionDispatchInfo.Capture(exception);
                break;
            }

            row.PublishedAt = timeProvider.GetUtcNow().UtcDateTime;
            published++;
            telemetry.RecordPublished(row);
            logger.LogInformation("Published {EventId} ({EventName}) for candidate {CandidateId}", row.EventId, row.Type, row.CandidateId);
        }

        if (published > 0)
        {
            // Not cancellable: these events are already in the hub. Marking them even during shutdown avoids
            // publishing them again after the restart.
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        sendFailure?.Throw();
        return rows.Count;
    }
}
