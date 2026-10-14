using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;
using TalentSync.Matching.Infrastructure.Llm.Fake;

namespace TalentSync.Matching.Infrastructure.Tests;

/// <summary>The enrichment adapter as <c>AddMatchingInfrastructure</c> registers it, over <see cref="FakeChatClient"/>.</summary>
public sealed class ChatEnrichmentServiceTests : IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose() => _providers.ForEach(p => p.Dispose());

    [Fact]
    public async Task Valid_ReturnsTheSuggestionWithTheModelId()
    {
        var (service, _) = Build();

        var outcome = await service.EnrichAsync("Backend developer. [llm:Valid]", TestContext.Current.CancellationToken);

        var enriched = Assert.IsType<EnrichmentOutcome.Enriched>(outcome);
        Assert.Equal(FakeChatClient.ModelId, enriched.ModelId);
        Assert.Equal(Seniority.Senior, enriched.Enrichment.Seniority);
        Assert.Equal(new Skill("C#"), enriched.Enrichment.Skills[0]);
    }

    [Theory]
    [InlineData("[llm:InvalidJson]")]
    [InlineData("[llm:OutOfRange]")]
    public async Task InvalidOutput_Fails(string marker)
    {
        var (service, _) = Build();

        var outcome = await service.EnrichAsync($"Backend developer. {marker}", TestContext.Current.CancellationToken);

        Assert.Equal(new EnrichmentOutcome.Failed(EnrichmentFailure.InvalidOutput), outcome);
    }

    [Fact]
    public async Task ModelThatDoesNotAnswer_FailsWithTimeout()
    {
        var (service, _) = Build(timeout: "00:00:00.200");

        var outcome = await service.EnrichAsync("Backend developer. [llm:Hang]", TestContext.Current.CancellationToken);

        Assert.Equal(new EnrichmentOutcome.Failed(EnrichmentFailure.Timeout), outcome);
    }

    [Fact]
    public async Task Shutdown_IsRethrownNotTurnedIntoAFailure()
    {
        var (service, _) = Build(timeout: "00:00:10");
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        shutdown.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.EnrichAsync("Backend developer. [llm:Hang]", shutdown.Token));
    }

    [Fact]
    public async Task TheModelReceivesTheMaskedCv()
    {
        var (service, fake) = Build();

        await service.EnrichAsync("Dev. Mail: ann.doe@example.com, phone +48 600 700 800.", TestContext.Current.CancellationToken);

        var received = Assert.Single(fake.ReceivedCvTexts);
        Assert.Equal("Dev. Mail: [email], phone [phone].", received);
    }

    [Fact]
    public async Task ProviderError_FailsAsUnavailable()
    {
        var client = new ThrowingChatClient();
        var (service, _) = Build(client: client);

        var outcome = await service.EnrichAsync("Backend developer.", TestContext.Current.CancellationToken);

        Assert.Equal(new EnrichmentOutcome.Failed(EnrichmentFailure.Unavailable), outcome);
    }

    [Fact]
    public async Task OpenCircuit_FailsWithoutCallingTheModel()
    {
        var client = new ThrowingChatClient();
        var (service, _) = Build(client: client);
        await service.EnrichAsync("Backend developer.", TestContext.Current.CancellationToken);
        await service.EnrichAsync("Backend developer.", TestContext.Current.CancellationToken);

        var outcome = await service.EnrichAsync("Backend developer.", TestContext.Current.CancellationToken);

        Assert.Equal(new EnrichmentOutcome.Failed(EnrichmentFailure.Unavailable), outcome);
        Assert.Equal(2, client.Calls);
    }

    private (IEnrichmentService Service, FakeChatClient Fake) Build(string timeout = "00:00:05", IChatClient? client = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // AddMatchingInfrastructure registers the Matching Platform client too; its section must exist.
                ["MatchingPlatform:BaseUrl"] = "http://matching.test",
                ["MatchingPlatform:Resilience:Retry:MaxRetryAttempts"] = "2",
                ["Llm:Provider"] = "Fake",
                ["Llm:Timeout"] = timeout,
                ["Llm:CircuitBreaker:FailureRatio"] = "0.5",
                ["Llm:CircuitBreaker:MinimumThroughput"] = "2",
                ["Llm:CircuitBreaker:SamplingDuration"] = "00:01:00",
                ["Llm:CircuitBreaker:BreakDuration"] = "00:01:00",
                ["Llm:Fake:Mode"] = "Valid",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMatchingInfrastructure(configuration);
        if (client is not null)
        {
            services.AddSingleton(client);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return (provider.GetRequiredService<IEnrichmentService>(), provider.GetRequiredService<FakeChatClient>());
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new HttpRequestException("Too many requests", inner: null, System.Net.HttpStatusCode.TooManyRequests);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
