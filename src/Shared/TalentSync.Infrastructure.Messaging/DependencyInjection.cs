using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalentSync.Infrastructure.Messaging.Processing;

namespace TalentSync.Infrastructure.Messaging;

/// <summary>Registers the consumer skeleton for one worker.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Consumes the hub with the worker's consumer group and hands every event to <typeparamref name="THandler"/>.
    /// The worker registers <c>IDbContextFactory&lt;TDbContext&gt;</c> itself; its <typeparamref name="TDbContext"/>
    /// applies <c>InboxEntryConfiguration</c> and <c>ParkedEventConfiguration</c> with its own schema.
    /// </summary>
    public static IServiceCollection AddMessaging<TDbContext, THandler>(this IServiceCollection services, IConfiguration configuration)
        where TDbContext : DbContext
        where THandler : class, IEventHandler
    {
        services.AddOptions<ConsumerOptions>()
            .Bind(configuration.GetSection(ConsumerOptions.SectionName))
            .Validate(o => o.IsValid(), $"'{ConsumerOptions.SectionName}' needs InitialBackoff > 0, MaxBackoff >= InitialBackoff and MaxUnclassifiedAttempts > 0.")
            .ValidateOnStart();

        var checkpointContainer = new BlobContainerClient(
            Required(configuration, "Checkpoints:BlobConnectionString"),
            Required(configuration, "Checkpoints:Container"));
        var processor = new EventProcessorClient(
            checkpointContainer,
            Required(configuration, "EventHubs:ConsumerGroup"),
            Required(configuration, "EventHubs:ConnectionString"),
            Required(configuration, "EventHubs:HubName"));

        // Singletons (invariant 9): the processor holds the AMQP connection and the partition ownership.
        services.AddSingleton(checkpointContainer);
        services.AddSingleton(processor);
        services.AddHostedService<EventProcessorHost>();

        services.AddScoped<IEventHandler, THandler>();
        services.AddScoped<IConsumerStore, ConsumerStore<TDbContext>>();
        services.AddScoped<EventProcessingLoop>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException($"Missing configuration '{key}'.");
}
