using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalentSync.Recruitment.Domain;
using TalentSync.Sync.Application;

namespace TalentSync.Sync.Infrastructure.Tests;

/// <summary>The CRM client with its real HTTP pipeline from <c>AddSyncInfrastructure</c>, over a stub handler.</summary>
public sealed class CrmClientTests : IDisposable
{
    private static readonly Candidate Jane = new("c-1", "Jane", "Doe", "jane@example.com", "+48 600 000 000", "Ten years of .NET");

    private readonly StubHandler _crm = new();
    private readonly ServiceProvider _services;
    private readonly ICrm _client;

    public CrmClientTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Crm:BaseUrl"] = "http://crm.test",
                ["Crm:Resilience:Retry:MaxRetryAttempts"] = "2",
                ["Crm:Resilience:Retry:Delay"] = "00:00:00",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSyncInfrastructure(configuration);
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _crm));
        _services = services.BuildServiceProvider();
        _client = _services.GetRequiredService<ICrm>();
    }

    public void Dispose() => _services.Dispose();

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Upsert_PutsTheContactWithoutCv(HttpStatusCode status)
    {
        _crm.Enqueue(status);

        var result = await _client.UpsertContactAsync(Jane, TestContext.Current.CancellationToken);

        Assert.Same(CrmResult.Done, result);
        var request = Assert.Single(_crm.Requests);
        Assert.Equal("PUT http://crm.test/contacts/c-1", request.Line);
        using var body = JsonDocument.Parse(request.Body!);
        var fields = body.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["firstName"] = "Jane",
                ["lastName"] = "Doe",
                ["email"] = "jane@example.com",
                ["phone"] = "+48 600 000 000",
            },
            fields);
    }

    [Fact]
    public async Task Upsert_ClientError_IsRejectedWithStatusAndEndpointOnly()
    {
        _crm.Enqueue(HttpStatusCode.BadRequest);

        var result = await _client.UpsertContactAsync(Jane, TestContext.Current.CancellationToken);

        Assert.Equal("400 PUT /contacts", Assert.IsType<CrmResult.RejectedResult>(result).Reason);
        Assert.Single(_crm.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Delete_OfAnExistingOrMissingContact_IsDone(HttpStatusCode status)
    {
        _crm.Enqueue(status);

        var result = await _client.DeleteContactAsync("c-1", TestContext.Current.CancellationToken);

        Assert.Same(CrmResult.Done, result);
        Assert.Equal("DELETE http://crm.test/contacts/c-1", Assert.Single(_crm.Requests).Line);
    }

    [Fact]
    public async Task Delete_OtherClientError_IsRejected()
    {
        _crm.Enqueue(HttpStatusCode.Conflict);

        var result = await _client.DeleteContactAsync("c-1", TestContext.Current.CancellationToken);

        Assert.Equal("409 DELETE /contacts", Assert.IsType<CrmResult.RejectedResult>(result).Reason);
    }

    [Fact]
    public async Task ServerError_IsRetriedByThePipelineThenThrown()
    {
        for (var i = 0; i < 3; i++)
        {
            _crm.Enqueue(HttpStatusCode.ServiceUnavailable);
        }

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.UpsertContactAsync(Jane, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(3, _crm.Requests.Count);
    }

    [Fact]
    public async Task ServerError_ThatRecovers_IsDone()
    {
        _crm.Enqueue(HttpStatusCode.ServiceUnavailable);
        _crm.Enqueue(HttpStatusCode.OK);

        var result = await _client.UpsertContactAsync(Jane, TestContext.Current.CancellationToken);

        Assert.Same(CrmResult.Done, result);
        Assert.Equal(2, _crm.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task WrongKey_IsThrownWithoutRetry(HttpStatusCode status)
    {
        _crm.Enqueue(status);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.DeleteContactAsync("c-1", TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.StatusCode);
        Assert.Single(_crm.Requests);
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
