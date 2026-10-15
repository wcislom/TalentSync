using Anthropic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Infrastructure.Llm;
using TalentSync.Matching.Infrastructure.Llm.Anthropic;
using TalentSync.Matching.Infrastructure.Llm.Fake;
using TalentSync.Matching.Infrastructure.MatchingPlatform;
using TalentSync.Matching.Infrastructure.Persistence;

namespace TalentSync.Matching.Infrastructure;

/// <summary>Registers the adapters of Matching.Worker. Implementations stay internal.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// <see cref="IMatchingPlatform"/> over the Matching Platform's REST API, <see cref="MatchingDbContext"/> and
    /// <see cref="IEnrichmentResultStore"/> over it, and <see cref="IEnrichmentService"/> over the LLM.
    /// Reads <c>MatchingPlatform:BaseUrl</c>, the standard resilience handler's options from
    /// <c>MatchingPlatform:Resilience</c>, <c>ConnectionStrings:Sql</c>, and the <c>Llm</c> section.
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

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .Validate(o => o.IsValid(),
                $"'{LlmOptions.SectionName}' needs a positive Timeout and Provider '{LlmOptions.FakeProvider}', or " +
                $"'{LlmOptions.AnthropicProvider}' with ApiKey, Model and a positive MaxOutputTokens.")
            .ValidateOnStart();
        services.AddSingleton<FakeChatClient>();
        services.AddSingleton<IChatClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
            return options.Provider switch
            {
                LlmOptions.FakeProvider => provider.GetRequiredService<FakeChatClient>(),
                LlmOptions.AnthropicProvider => CreateAnthropicChatClient(options.Anthropic),
                _ => throw new InvalidOperationException($"Unknown '{LlmOptions.SectionName}:Provider'."),
            };
        });
        services.AddSingleton<IEnrichmentService, ChatEnrichmentService>();

        return services;
    }

    private static IChatClient CreateAnthropicChatClient(AnthropicLlmOptions options) =>
        // The SDK retries 408/429/5xx twice by default. ADR 007: no retry for the LLM, a failure ends in NotEnriched
        // and the circuit breaker in ChatEnrichmentService must see it. The timeout comes from the same pipeline.
        new AnthropicClient { ApiKey = options.ApiKey, MaxRetries = 0 }
            .AsIChatClient(options.Model, options.MaxOutputTokens);
}
