using TalentSync.Matching.Infrastructure;

namespace TalentSync.Matching.Worker;

/// <summary>Composition root of Matching.Worker: every service registration of the host lives here.</summary>
internal static class DependencyInjection
{
    public static IServiceCollection AddMatchingWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMatchingInfrastructure(configuration);

        return services;
    }
}
