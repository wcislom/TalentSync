using TalentSync.Sync.Infrastructure;

namespace TalentSync.Sync.Worker;

/// <summary>Composition root of Sync.Worker: every service registration of the host lives here.</summary>
internal static class DependencyInjection
{
    public static IServiceCollection AddSyncWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSyncInfrastructure(configuration);

        return services;
    }
}
