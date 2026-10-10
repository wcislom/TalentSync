using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalentSync.Infrastructure.Teamtailor.Webhooks;

namespace TalentSync.Infrastructure.Teamtailor;

/// <summary>
/// The only public entry point of the adapter besides the webhook interfaces (dependency rules in
/// <c>docs/architecture.md</c>). Implementations stay internal.
/// </summary>
public static class DependencyInjection
{
    /// <param name="configuration">
    /// Not read yet. The signature verifier and the API client will bind the <c>Teamtailor</c> section from it.
    /// </param>
    public static IServiceCollection AddTeamtailor(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Stateless, so one instance serves every request.
        services.AddSingleton<IWebhookParser, TeamtailorWebhookParser>();
        return services;
    }
}
