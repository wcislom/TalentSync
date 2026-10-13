using TalentSync.Infrastructure.Messaging;
using TalentSync.Infrastructure.Teamtailor;
using TalentSync.Matching.Application.UseCases;
using TalentSync.Matching.Infrastructure;
using TalentSync.Matching.Infrastructure.Persistence;
using TalentSync.Matching.Worker.Events;

namespace TalentSync.Matching.Worker;

/// <summary>Composition root of Matching.Worker: every service registration of the host lives here.</summary>
internal static class DependencyInjection
{
    public static IServiceCollection AddMatchingWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTeamtailorApi(configuration);
        services.AddMatchingInfrastructure(configuration);
        services.AddScoped<CreateConsultantProfileUseCase>();
        services.AddMessaging<MatchingDbContext, JobApplicationEventHandler>(configuration);
        services.AddHealthChecks();

        return services;
    }
}
