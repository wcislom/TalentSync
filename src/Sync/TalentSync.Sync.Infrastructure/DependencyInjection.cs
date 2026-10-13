using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TalentSync.Sync.Application;
using TalentSync.Sync.Infrastructure.Crm;

namespace TalentSync.Sync.Infrastructure;

/// <summary>Registers the adapters of Sync.Worker. Implementations stay internal.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// <see cref="ICrm"/> over the CRM's REST API. Reads <c>Crm:BaseUrl</c>, and the standard resilience handler's
    /// options from <c>Crm:Resilience</c>.
    /// </summary>
    public static IServiceCollection AddSyncInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(CrmOptions.SectionName);
        services.AddOptions<CrmOptions>()
            .Bind(section)
            .Validate(o => o.IsValid(), $"'{CrmOptions.SectionName}' needs an absolute BaseUrl.")
            .ValidateOnStart();

        // Short blips only: attempt timeout, a few retries, circuit breaker; long outages are waited out by the
        // handler loop. The standard handler retries PUT and DELETE too. Harmless here: both are keyed by the
        // candidate id, so a retry after a timeout whose request did reach the CRM repeats the same effect (invariant 2).
        services.AddHttpClient<ICrm, CrmClient>((provider, client) =>
                client.BaseAddress = provider.GetRequiredService<IOptions<CrmOptions>>().Value.BaseUrl)
            .AddStandardResilienceHandler(section.GetSection("Resilience"));

        return services;
    }
}
