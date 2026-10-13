using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using TalentSync.Infrastructure.Teamtailor.Api;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Infrastructure.Teamtailor.Tests;

/// <summary>
/// The client with its real HTTP pipeline from <c>AddTeamtailorApi</c>, over a stub handler: status mapping,
/// headers and resilience. Mapping into our model is covered by <see cref="TeamtailorMapperTests"/>.
/// </summary>
public sealed class TeamtailorApiClientTests : IDisposable
{
    private const string CandidateJson = """{ "data": { "id": "c-1", "type": "candidates" } }""";

    private readonly StubHandler _api = new();
    private readonly RecordingTimeProvider _time = new();
    private readonly ServiceProvider _services;
    private readonly TeamtailorApiClient _client;

    public TeamtailorApiClientTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Teamtailor:BaseUrl"] = "http://teamtailor.test",
                ["Teamtailor:ApiKey"] = "test-key",
                ["Teamtailor:ApiVersion"] = "20240904",
                ["Teamtailor:QualifiedStageName"] = "Qualified",
                ["Teamtailor:Resilience:Retry:MaxRetryAttempts"] = "2",
                ["Teamtailor:Resilience:Retry:Delay"] = "00:00:00",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddTeamtailorApi(configuration);
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _api));
        _services = services.BuildServiceProvider();
        _client = _services.GetRequiredService<TeamtailorApiClient>();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task RecruitmentSource_FetchesThroughTheClientAndMaps()
    {
        _api.Enqueue(HttpStatusCode.OK, """
            {
              "data": {
                "id": "ja-1",
                "type": "job-applications",
                "relationships": {
                  "candidate": { "data": { "id": "c-1", "type": "candidates" } },
                  "stage": { "data": { "id": "st-4", "type": "stages" } }
                }
              },
              "included": [ { "id": "st-4", "type": "stages", "attributes": { "name": "Qualified" } } ]
            }
            """);
        var source = _services.GetRequiredService<IRecruitmentSource>();

        var result = await source.GetJobApplicationAsync("ja-1", TestContext.Current.CancellationToken);

        var found = Assert.IsType<FetchResult<JobApplication>.Found>(result);
        Assert.Equal(new JobApplication("ja-1", "c-1", ApplicationStatus.Qualified), found.Value);
        Assert.Equal("http://teamtailor.test/v1/job-applications/ja-1?include=stage", Assert.Single(_api.Requests).Uri);
    }

    [Fact]
    public async Task Candidate_IsReadWithApiHeaders()
    {
        _api.Enqueue(HttpStatusCode.OK, """
            {
              "data": {
                "id": "c-1",
                "type": "candidates",
                "attributes": { "first-name": "Jane", "resume-text": "Ten years of .NET", "unknown-attribute": 1 }
              }
            }
            """);

        var result = await _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken);

        var data = Assert.IsType<FetchResult<CandidateResponseDto>.Found>(result).Value.Data;
        Assert.Equal("c-1", data?.Id);
        Assert.Equal("Jane", data?.Attributes?.FirstName);
        Assert.Equal("Ten years of .NET", data?.Attributes?.ResumeText);
        var request = Assert.Single(_api.Requests);
        Assert.Equal("http://teamtailor.test/v1/candidates/c-1", request.Uri);
        Assert.Equal("Token token=test-key", request.Authorization);
        Assert.Equal("20240904", request.ApiVersion);
    }

    [Fact]
    public async Task NotFound_IsDeleted()
    {
        _api.Enqueue(HttpStatusCode.NotFound, """{ "errors": [ { "status": "404" } ] }""");

        var result = await _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken);

        Assert.IsType<FetchResult<CandidateResponseDto>.Deleted>(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task UnexpectedClientError_IsRejectedWithStatusAndEndpointOnly(HttpStatusCode status)
    {
        _api.Enqueue(status, """{ "errors": [ { "detail": "jane@example.com is invalid" } ] }""");

        var result = await _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken);

        Assert.Equal($"{(int)status} GET /v1/candidates", Assert.IsType<FetchResult<CandidateResponseDto>.Rejected>(result).Reason);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task UnreadableJson_IsRejected()
    {
        _api.Enqueue(HttpStatusCode.OK, "not json");

        var result = await _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken);

        Assert.Equal("unreadable JSON from GET /v1/candidates", Assert.IsType<FetchResult<CandidateResponseDto>.Rejected>(result).Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task WrongKey_IsThrownWithoutRetry(HttpStatusCode status)
    {
        _api.Enqueue(status, "{}");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.StatusCode);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task ServerError_IsRetriedByThePipelineThenThrown()
    {
        for (var i = 0; i < 3; i++)
        {
            _api.Enqueue(HttpStatusCode.ServiceUnavailable, "{}");
        }

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(3, _api.Requests.Count);
    }

    [Fact]
    public async Task RateLimit_WaitsForTheResetAndRetriesOnce()
    {
        _api.Enqueue(HttpStatusCode.TooManyRequests, "{}", reset: "1");
        _api.Enqueue(HttpStatusCode.OK, CandidateJson);

        var result = await _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken);

        Assert.IsType<FetchResult<CandidateResponseDto>.Found>(result);
        Assert.Equal(2, _api.Requests.Count);
        Assert.Contains(TimeSpan.FromSeconds(1), _time.Delays);
    }

    [Fact]
    public async Task SecondRateLimit_IsThrownAfterExactlyTwoCalls()
    {
        _api.Enqueue(HttpStatusCode.TooManyRequests, "{}", reset: "0");
        _api.Enqueue(HttpStatusCode.TooManyRequests, "{}", reset: "0");
        _api.Enqueue(HttpStatusCode.OK, CandidateJson);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task RateLimitWithoutReset_IsThrownWithoutRetry()
    {
        _api.Enqueue(HttpStatusCode.TooManyRequests, "{}");
        _api.Enqueue(HttpStatusCode.OK, CandidateJson);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetCandidateAsync("c-1", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Single(_api.Requests);
    }

    // The reason the standard retry and circuit breaker get their own predicate. If this ever fails, the library
    // stopped treating 429 as transient and the override may be dropped.
    [Fact]
    public void StandardPredicate_Treats429AsTransient()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

#pragma warning disable xUnit1051 // The overload taking a token is experimental (EXTEXP0001).
        Assert.True(HttpClientResiliencePredicates.IsTransient(Outcome.FromResult(response)));
#pragma warning restore xUnit1051
    }

    private sealed record RecordedRequest(string Uri, string? Authorization, string? ApiVersion);

    /// <summary>Answers from a queue, one response per request, and records what was sent.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body, string? Reset)> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body, string? reset = null) => _responses.Enqueue((status, body, reset));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Api-Version", out var versions) ? versions.Single() : null));

            var (status, body, reset) = _responses.Dequeue();
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/vnd.api+json"),
            };
            if (reset is not null)
            {
                response.Headers.Add("X-Rate-Limit-Reset", reset);
            }

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Records every timer the pipeline creates and runs it on the system clock. Firing timers early is not an option:
    /// the attempt and total timeouts are timers too.
    /// </summary>
    private sealed class RecordingTimeProvider : TimeProvider
    {
        private readonly List<TimeSpan> _delays = [];

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_delays)
                {
                    return [.. _delays];
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_delays)
            {
                _delays.Add(dueTime);
            }

            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
