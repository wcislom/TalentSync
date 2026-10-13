using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TalentSync.Sync.Application.Ports;
using TalentSync.Sync.Infrastructure.Crm;
using TalentSync.Sync.Infrastructure.Persistence;

namespace TalentSync.Sync.Infrastructure;

/// <summary>Registers the adapters of Sync.Worker. Implementations stay internal.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// <see cref="ICrm"/> over the CRM's REST API, and <see cref="SyncDbContext"/>. Reads <c>Crm:BaseUrl</c>, the standard
    /// resilience handler's options from <c>Crm:Resilience</c>, and <c>ConnectionStrings:Sql</c>.
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

        services.AddDbContextFactory<SyncDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Sql")
                ?? throw new InvalidOperationException("Missing configuration 'ConnectionStrings:Sql'."),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SyncDbContext.Schema)));

        return services;
    }
}
