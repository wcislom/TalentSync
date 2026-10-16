using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TalentSync.Matching.Infrastructure.Llm;
using TalentSync.Matching.Infrastructure.Llm.Fake;

namespace TalentSync.Matching.Infrastructure.Tests;

/// <summary>Which <see cref="IChatClient"/> <c>AddMatchingInfrastructure</c> picks from <c>Llm:Provider</c>. No model is called.</summary>
public sealed class LlmProviderRegistrationTests : IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose() => _providers.ForEach(p => p.Dispose());

    [Fact]
    public void Fake_ResolvesTheFakeChatClientSingletonBehindTelemetry()
    {
        var provider = Build(new() { ["Llm:Provider"] = "Fake" });

        var client = provider.GetRequiredService<IChatClient>();

        // The same instance the integration tests count calls on (scenario M5).
        Assert.IsType<TelemetryChatClient>(client);
        Assert.Same(provider.GetRequiredService<FakeChatClient>(), client.GetService<FakeChatClient>());
    }

    [Fact]
    public void Anthropic_ResolvesAClientForTheConfiguredModel()
    {
        var provider = Build(new()
        {
            ["Llm:Provider"] = "Anthropic",
            ["Llm:Anthropic:ApiKey"] = "test-key",
            ["Llm:Anthropic:Model"] = "claude-haiku-5-5",
            ["Llm:Anthropic:MaxOutputTokens"] = "8000",
            ["Llm:Anthropic:MaxRetries"] = "2",
        });

        var metadata = provider.GetRequiredService<IChatClient>().GetService<ChatClientMetadata>();
        Assert.Equal("claude-haiku-5-5", metadata?.DefaultModelId);
    }

    [Theory]
    [InlineData("Anthropic", null, "claude-haiku-5-5", "8000", "2")]
    [InlineData("Anthropic", "test-key", null, "8000", "2")]
    [InlineData("Anthropic", "test-key", "claude-haiku-5-5", "0", "2")]
    [InlineData("Anthropic", "test-key", "claude-haiku-5-5", "8000", "-1")]
    [InlineData("Gemini", "test-key", "claude-haiku-5-5", "8000", "2")]
    public void InvalidConfiguration_FailsValidation(
        string providerName, string? apiKey, string? model, string maxOutputTokens, string maxRetries)
    {
        var provider = Build(new()
        {
            ["Llm:Provider"] = providerName,
            ["Llm:Anthropic:ApiKey"] = apiKey,
            ["Llm:Anthropic:Model"] = model,
            ["Llm:Anthropic:MaxOutputTokens"] = maxOutputTokens,
            ["Llm:Anthropic:MaxRetries"] = maxRetries,
        });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<LlmOptions>>().Value);
    }

    private ServiceProvider Build(Dictionary<string, string?> llm)
    {
        var settings = new Dictionary<string, string?>
        {
            // AddMatchingInfrastructure registers the Matching Platform client too; its section must exist.
            ["MatchingPlatform:BaseUrl"] = "http://matching.test",
            ["MatchingPlatform:Resilience:Retry:MaxRetryAttempts"] = "2",
            ["Llm:Timeout"] = "00:00:05",
        };
        foreach (var (key, value) in llm)
        {
            settings[key] = value;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMatchingInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }
}
