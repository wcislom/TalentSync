using Azure.Messaging.EventHubs.Producer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var eventHubsConnectionString = configuration["EventHubs:ConnectionString"]
            ?? throw new InvalidOperationException("Missing configuration 'EventHubs:ConnectionString'.");
        var hubName = configuration["EventHubs:HubName"]
            ?? throw new InvalidOperationException("Missing configuration 'EventHubs:HubName'.");

        services.AddDbContext<IngressDbContext>(options => options.UseSqlServer(
            sqlConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IngressDbContext.Schema)));

        // Singleton (invariant 9): it holds the AMQP connection. The container disposes it on shutdown.
        services.AddSingleton(_ => new EventHubProducerClient(eventHubsConnectionString, hubName));

        services.AddOptions<OutboxRelayOptions>()
            .Bind(configuration.GetSection(OutboxRelayOptions.SectionName))
            .Validate(o => o.IsValid(), $"'{OutboxRelayOptions.SectionName}' needs BatchSize > 0, PollInterval > 0 and MaxBackoff >= PollInterval.")
            .ValidateOnStart();
        services.AddHostedService<OutboxRelay>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddTeamtailor(configuration);

        return services;
    }
}
