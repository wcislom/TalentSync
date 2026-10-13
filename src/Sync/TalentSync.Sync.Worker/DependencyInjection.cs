using TalentSync.Infrastructure.Messaging;
using TalentSync.Infrastructure.Teamtailor;
using TalentSync.Sync.Application.UseCases;
using TalentSync.Sync.Infrastructure;
using TalentSync.Sync.Infrastructure.Persistence;
using TalentSync.Sync.Worker.Events;

namespace TalentSync.Sync.Worker;

/// <summary>Composition root of Sync.Worker: every service registration of the host lives here.</summary>
internal static class DependencyInjection
{
    public static IServiceCollection AddSyncWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTeamtailorApi(configuration);
        services.AddSyncInfrastructure(configuration);
        services.AddScoped<SyncCandidateToCrmUseCase>();
        services.AddMessaging<SyncDbContext, CandidateEventHandler>(configuration);
        services.AddHealthChecks();

        return services;
    }
}
