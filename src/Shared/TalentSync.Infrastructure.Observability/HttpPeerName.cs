using System.Diagnostics;
using Microsoft.Extensions.Http;

namespace TalentSync.Infrastructure.Observability;

/// <summary>
/// Names an outgoing HTTP span after the system it calls, e.g. <c>PUT crm /contacts</c> instead of <c>PUT</c>.
/// The system is the name of the <c>IHttpClientFactory</c> client that sent the request, so the adapters only name
/// their clients and never reference this project.
/// </summary>
internal static class HttpPeerName
{
    private static readonly HttpRequestOptionsKey<string> Key = new("talentsync.peer");

    /// <summary>
    /// Runs before the span exists: the span is started at the bottom of the handler chain, so the name travels on
    /// the request.
    /// </summary>
    internal sealed class Filter : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (!string.IsNullOrEmpty(builder.Name))
            {
                builder.AdditionalHandlers.Insert(0, new Handler(builder.Name));
            }
        };
    }

    public static void Enrich(Activity activity, HttpRequestMessage request)
    {
        if (request.Options.TryGetValue(Key, out var peer))
        {
            activity.DisplayName = $"{request.Method} {peer} {Resource(request.RequestUri)}";
            activity.SetTag("peer.service", peer);
        }
    }

    // The path without its last segment, which is the id in every API we call (/contacts/{id}, /v1/candidates/{id}).
    // A heuristic: it keeps the span name readable and free of ids.
    private static string Resource(Uri? uri)
    {
        var path = uri?.AbsolutePath.TrimEnd('/') ?? "";
        var lastSlash = path.LastIndexOf('/');
        return lastSlash > 0 ? path[..lastSlash] : path;
    }

    private sealed class Handler(string peer) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Options.Set(Key, peer);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
