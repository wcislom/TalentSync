using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Infrastructure.Tests;

/// <summary>The Matching Platform client with its real HTTP pipeline from <c>AddMatchingInfrastructure</c>, over a stub handler.</summary>
public sealed class MatchingPlatformClientTests : IDisposable
{
    private static readonly ConsultantProfile Profile = ConsultantProfile.NotEnriched(new CandidateId("c-1"));

    private readonly StubHandler _platform = new();
    private readonly ServiceProvider _services;
    private readonly IMatchingPlatform _client;

    public MatchingPlatformClientTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MatchingPlatform:BaseUrl"] = "http://matching.test",
                ["MatchingPlatform:Resilience:Retry:MaxRetryAttempts"] = "2",
                ["MatchingPlatform:Resilience:Retry:Delay"] = "00:00:00",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMatchingInfrastructure(configuration);
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _platform));
        _services = services.BuildServiceProvider();
        _client = _services.GetRequiredService<IMatchingPlatform>();
    }

    public void Dispose() => _services.Dispose();

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Upsert_PutsTheProfileByCandidateId(HttpStatusCode status)
    {
        _platform.Enqueue(status);

        var result = await _client.UpsertProfileAsync(Profile, TestContext.Current.CancellationToken);

        Assert.Same(MatchingPlatformResult.Done, result);
        var request = Assert.Single(_platform.Requests);
        Assert.Equal("PUT http://matching.test/profiles/c-1", request.Line);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal(["skills", "seniority", "confidence", "enrichmentStatus"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(0, root.GetProperty("skills").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("seniority").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("confidence").ValueKind);
        Assert.Equal("NotEnriched", root.GetProperty("enrichmentStatus").GetString());
    }

    [Fact]
    public async Task Upsert_EnrichedProfile_SendsTheEnrichment()
    {
        _platform.Enqueue(HttpStatusCode.OK);
        var profile = ConsultantProfile.Enriched(
            new CandidateId("c-2"),
            new Enrichment([new Skill("C#"), new Skill("Azure")], Seniority.Senior, new Confidence(0.8)));

        await _client.UpsertProfileAsync(profile, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(_platform.Requests).Body!);
        var root = body.RootElement;
        Assert.Equal(["C#", "Azure"], root.GetProperty("skills").EnumerateArray().Select(skill => skill.GetString()));
        Assert.Equal("Senior", root.GetProperty("seniority").GetString());
        Assert.Equal(0.8, root.GetProperty("confidence").GetDouble());
        Assert.Equal("Enriched", root.GetProperty("enrichmentStatus").GetString());
    }

    [Fact]
    public async Task Upsert_ClientError_IsRejectedWithStatusAndEndpointOnly()
    {
        _platform.Enqueue(HttpStatusCode.BadRequest);

        var result = await _client.UpsertProfileAsync(Profile, TestContext.Current.CancellationToken);

        Assert.Equal("400 PUT /profiles", Assert.IsType<MatchingPlatformResult.RejectedResult>(result).Reason);
        Assert.Single(_platform.Requests);
    }

    [Fact]
    public async Task ServerError_IsRetriedByThePipelineThenThrown()
    {
        for (var i = 0; i < 3; i++)
        {
            _platform.Enqueue(HttpStatusCode.ServiceUnavailable);
        }

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.UpsertProfileAsync(Profile, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(3, _platform.Requests.Count);
    }

    [Fact]
    public async Task ServerError_ThatRecovers_IsDone()
    {
        _platform.Enqueue(HttpStatusCode.ServiceUnavailable);
        _platform.Enqueue(HttpStatusCode.OK);

        var result = await _client.UpsertProfileAsync(Profile, TestContext.Current.CancellationToken);

        Assert.Same(MatchingPlatformResult.Done, result);
        Assert.Equal(2, _platform.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task WrongKey_IsThrownWithoutRetry(HttpStatusCode status)
    {
        _platform.Enqueue(status);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.UpsertProfileAsync(Profile, TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.StatusCode);
        Assert.Single(_platform.Requests);
    }

    private sealed record RecordedRequest(string Line, string? Body);

    /// <summary>Answers from a queue, one status per request, and records what was sent.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status) => _statuses.Enqueue(status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest($"{request.Method} {request.RequestUri}", body));
            return new HttpResponseMessage(_statuses.Dequeue());
        }
    }
}
