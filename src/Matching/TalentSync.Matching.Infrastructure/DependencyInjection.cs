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
        // The client name labels its HTTP spans and resilience logs.
        services.AddHttpClient<IMatchingPlatform, MatchingPlatformClient>("matching-platform", (provider, client) =>
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
                $"'{LlmOptions.AnthropicProvider}' with ApiKey, Model, a positive MaxOutputTokens and a non-negative MaxRetries.")
            .ValidateOnStart();
        services.AddSingleton<FakeChatClient>();
        services.AddSingleton<IChatClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
            IChatClient inner = options.Provider switch
            {
                LlmOptions.FakeProvider => provider.GetRequiredService<FakeChatClient>(),
                LlmOptions.AnthropicProvider => CreateAnthropicChatClient(options.Anthropic),
                _ => throw new InvalidOperationException($"Unknown '{LlmOptions.SectionName}:Provider'."),
            };
            return new TelemetryChatClient(inner);
        });

        // Singletons, so the circuit breaker sees every call of the worker.
        services.AddSingleton<ChatEnrichmentService>();
        services.AddSingleton<IEnrichmentService>(provider =>
            new TelemetryEnrichmentService(provider.GetRequiredService<ChatEnrichmentService>()));

        return services;
    }

    // Internal so the prompt evaluation (ADR 009) builds the client exactly as the worker does.
    internal static IChatClient CreateAnthropicChatClient(AnthropicLlmOptions options) =>
        new AnthropicClient { ApiKey = options.ApiKey, MaxRetries = options.MaxRetries }
            .AsIChatClient(options.Model, options.MaxOutputTokens);
}
