using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalentSync.Matching.Infrastructure.Persistence;

namespace TalentSync.Matching.Infrastructure;

/// <summary>Registers the adapters of Matching.Worker. Implementations stay internal.</summary>
public static class DependencyInjection
{
    /// <summary><see cref="MatchingDbContext"/>. Reads <c>ConnectionStrings:Sql</c>.</summary>
    public static IServiceCollection AddMatchingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<MatchingDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Sql")
                ?? throw new InvalidOperationException("Missing configuration 'ConnectionStrings:Sql'."),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", MatchingDbContext.Schema)));

        return services;
    }
}
