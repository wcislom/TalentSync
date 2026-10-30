using System.Diagnostics.Metrics;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalentSync.Infrastructure.Observability;
using TalentSync.Infrastructure.Teamtailor;
using TalentSync.Ingress.Api.Outbox;
using TalentSync.Ingress.Api.Persistence;

namespace TalentSync.Ingress.Api;

/// <summary>Composition root of Ingress.Api: every service registration of the host lives here.</summary>
internal static class DependencyInjection
{
    public static IServiceCollection AddIngress(this IServiceCollection services, IConfiguration configuration)
    {
        var sqlConnectionString = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing configuration 'ConnectionStrings:Sql'.");
        var hubName = configuration["EventHubs:HubName"]
            ?? throw new InvalidOperationException("Missing configuration 'EventHubs:HubName'.");

        services.AddDbContextFactory<IngressDbContext>(options => options.UseSqlServer(
            sqlConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IngressDbContext.Schema)));

        services.AddSingleton(provider => new IngressTelemetry(provider.GetRequiredService<IMeterFactory>(), hubName));

        // Off in the API replicas of a cluster, on in exactly one relay pod (invariant 13, ADR 011).
        var relayEnabled = configuration.GetValue<bool?>($"{OutboxRelayOptions.SectionName}:Enabled")
            ?? throw new InvalidOperationException($"Missing configuration '{OutboxRelayOptions.SectionName}:Enabled'.");
        if (relayEnabled)
        {
            AddOutboxRelay(services, configuration, hubName);
        }

        services.AddHealthChecks();
        services.TryAddSingleton(TimeProvider.System);
        services.AddTeamtailorWebhooks(configuration);
        services.AddObservability(configuration);

        return services;
    }

    private static void AddOutboxRelay(IServiceCollection services, IConfiguration configuration, string hubName)
    {
        var eventHubsConnectionString = configuration["EventHubs:ConnectionString"]
            ?? throw new InvalidOperationException("Missing configuration 'EventHubs:ConnectionString'.");

        // Singleton (invariant 9): it holds the AMQP connection. The container disposes it on shutdown.
        // The SDK retries a send on its own before the relay sees a failure: up to MaxRetries attempts of TryTimeout each.
        var producerOptions = new EventHubProducerClientOptions
        {
            RetryOptions = new EventHubsRetryOptions
            {
                TryTimeout = configuration.GetValue<TimeSpan?>("EventHubs:TryTimeout")
                    ?? throw new InvalidOperationException("Missing configuration 'EventHubs:TryTimeout'."),
                MaximumRetries = configuration.GetValue<int?>("EventHubs:MaxRetries")
                    ?? throw new InvalidOperationException("Missing configuration 'EventHubs:MaxRetries'."),
            },
        };
        services.AddSingleton(_ => new EventHubProducerClient(eventHubsConnectionString, hubName, producerOptions));

        services.AddOptions<OutboxRelayOptions>()
            .Bind(configuration.GetSection(OutboxRelayOptions.SectionName))
            .Validate(o => o.IsValid(), $"'{OutboxRelayOptions.SectionName}' needs BatchSize > 0, PollInterval > 0 and MaxBackoff >= PollInterval.")
            .ValidateOnStart();
        services.AddSingleton<OutboxPublisher>();
        services.AddHostedService<OutboxRelay>();
    }
}
