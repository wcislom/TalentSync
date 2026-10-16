using System.Diagnostics;
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

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? throw new InvalidOperationException("Request without a URI.");
        if (!_invokers.TryGetValue(host, out var invoker))
        {
            throw new InvalidOperationException($"No in-process server for host '{host}'.");
        }

        // Behave like the network for trace context, so scenario O1 can show that a host ignores its caller's context.
        // SocketsHttpHandler, which this handler replaces, writes the context to the request headers; and the in-process
        // server must not inherit the caller's span as an ambient parent, which no network passes on. The change to
        // Activity.Current stays inside this async method.
        if (Activity.Current is { } activity)
        {
            DistributedContextPropagator.Current.Inject(activity, request, static (carrier, name, value) =>
                ((HttpRequestMessage)carrier!).Headers.TryAddWithoutValidation(name, value));
        }

        Activity.Current = null;
        return await invoker.SendAsync(request, cancellationToken);
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
