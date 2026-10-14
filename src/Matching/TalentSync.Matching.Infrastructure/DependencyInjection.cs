using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Infrastructure.MatchingPlatform;
using TalentSync.Matching.Infrastructure.Persistence;

namespace TalentSync.Matching.Infrastructure;

/// <summary>Registers the adapters of Matching.Worker. Implementations stay internal.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// <see cref="IMatchingPlatform"/> over the Matching Platform's REST API, <see cref="MatchingDbContext"/> and
    /// <see cref="IEnrichmentResultStore"/> over it.
    /// Reads <c>MatchingPlatform:BaseUrl</c>, the standard resilience handler's options from
    /// <c>MatchingPlatform:Resilience</c>, and <c>ConnectionStrings:Sql</c>.
    /// </summary>
    public static IServiceCollection AddMatchingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MatchingPlatformOptions.SectionName);
        services.AddOptions<MatchingPlatformOptions>()
            .Bind(section)
            .Validate(o => o.IsValid(), $"'{MatchingPlatformOptions.SectionName}' needs an absolute BaseUrl.")
            .ValidateOnStart();

        // Short blips only: attempt timeout, a few retries, circuit breaker; long outages are waited out by the
        // handler loop. Retrying the PUT is harmless: it is keyed by the candidate id, so a retry after a timeout
        // whose request did reach the platform repeats the same effect (invariant 2).
        services.AddHttpClient<IMatchingPlatform, MatchingPlatformClient>((provider, client) =>
                client.BaseAddress = provider.GetRequiredService<IOptions<MatchingPlatformOptions>>().Value.BaseUrl)
            .AddStandardResilienceHandler(section.GetSection("Resilience"));

        services.AddDbContextFactory<MatchingDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Sql")
                ?? throw new InvalidOperationException("Missing configuration 'ConnectionStrings:Sql'."),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", MatchingDbContext.Schema)));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IEnrichmentResultStore, EnrichmentResultStore>();

        return services;
    }
}
