using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalentSync.Infrastructure.Teamtailor;
using TalentSync.Ingress.Api.Persistence;

namespace TalentSync.Ingress.Api;

/// <summary>Composition root of Ingress.Api: every service registration of the host lives here.</summary>
internal static class DependencyInjection
{
    public static IServiceCollection AddIngress(this IServiceCollection services, IConfiguration configuration)
    {
        var sqlConnectionString = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing configuration 'ConnectionStrings:Sql'.");

        services.AddDbContext<IngressDbContext>(options => options.UseSqlServer(
            sqlConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IngressDbContext.Schema)));

        services.TryAddSingleton(TimeProvider.System);
        services.AddTeamtailor(configuration);

        return services;
    }
}
