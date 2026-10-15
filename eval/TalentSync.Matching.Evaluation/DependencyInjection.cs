using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Infrastructure.Llm;
using TalentSync.Matching.Infrastructure.Llm.Fake;

namespace TalentSync.Matching.Evaluation;

internal static class DependencyInjection
{
    /// <summary>
    /// <see cref="ChatEnrichmentService"/> as Matching.Worker builds it, without <c>AddMatchingInfrastructure</c>:
    /// the evaluation needs neither SQL nor the Matching Platform (ADR 009). Reads the <c>Llm</c> and <c>Evaluation</c> sections.
    /// </summary>
    public static IServiceCollection AddEvaluation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EvaluationOptions>()
            .Bind(configuration.GetSection(EvaluationOptions.SectionName))
            .Validate(o => o.IsValid(),
                $"'{EvaluationOptions.SectionName}' needs a positive RunsPerCase, CasesDirectory and OutputDirectory.");

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .Validate(o => o.IsValid(),
                $"'{LlmOptions.SectionName}' needs a positive Timeout and Provider '{LlmOptions.FakeProvider}', or " +
                $"'{LlmOptions.AnthropicProvider}' with ApiKey, Model, a positive MaxOutputTokens and a non-negative MaxRetries.");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<FakeChatClient>();
        services.AddSingleton<IChatClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
            return options.Provider switch
            {
                LlmOptions.FakeProvider => provider.GetRequiredService<FakeChatClient>(),
                LlmOptions.AnthropicProvider => Infrastructure.DependencyInjection.CreateAnthropicChatClient(options.Anthropic),
                _ => throw new InvalidOperationException($"Unknown '{LlmOptions.SectionName}:Provider'."),
            };
        });
        services.AddSingleton<IEnrichmentService, ChatEnrichmentService>();
        services.AddSingleton<EvaluationRunner>();

        return services;
    }
}
