using Microsoft.AspNetCore.TestHost;

namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>
/// Primary handler of the hosts' outgoing HTTP clients: sends each request to the in-process server named by the
/// request's host (e.g. <c>crm.test</c>). Installed below the clients' own handlers, so their resilience pipelines
/// (retry, circuit breaker, 429 handling) run unchanged.
/// </summary>
public sealed class HostRouter(IReadOnlyDictionary<string, TestServer> servers) : HttpMessageHandler
{
    private readonly Dictionary<string, HttpMessageInvoker> _invokers =
        servers.ToDictionary(server => server.Key, server => new HttpMessageInvoker(server.Value.CreateHandler()));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? throw new InvalidOperationException("Request without a URI.");
        return _invokers.TryGetValue(host, out var invoker)
            ? invoker.SendAsync(request, cancellationToken)
            : throw new InvalidOperationException($"No in-process server for host '{host}'.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var invoker in _invokers.Values)
            {
                invoker.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
