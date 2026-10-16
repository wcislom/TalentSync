using System.Diagnostics.Metrics;
using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalentSync.Infrastructure.Messaging.EventHubs;
using TalentSync.Infrastructure.Messaging.Handling;
using TalentSync.Infrastructure.Messaging.Persistence;

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

        var hubName = Required(configuration, "EventHubs:HubName");
        var consumerGroup = Required(configuration, "EventHubs:ConsumerGroup");
        var checkpointContainer = new BlobContainerClient(
            Required(configuration, "Checkpoints:BlobConnectionString"),
            Required(configuration, "Checkpoints:Container"));
        var processor = new EventProcessorClient(
            checkpointContainer, consumerGroup, Required(configuration, "EventHubs:ConnectionString"), hubName);

        // Singletons (invariant 9): the processor holds the AMQP connection and the partition ownership.
        services.AddSingleton(checkpointContainer);
        services.AddSingleton(processor);
        services.AddHostedService<EventProcessorHost>();

        services.AddScoped<IEventHandler, THandler>();
        services.AddScoped<IConsumerStore, ConsumerStore<TDbContext>>();
        services.AddScoped<EventProcessingLoop>();
        services.TryAddSingleton(TimeProvider.System);
        // IMeterFactory comes from the host (the generic host registers it).
        services.AddSingleton(provider => new ConsumerTelemetry(
            provider.GetRequiredService<IMeterFactory>(), provider.GetRequiredService<TimeProvider>(), hubName, consumerGroup));

        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException($"Missing configuration '{key}'.");
}
