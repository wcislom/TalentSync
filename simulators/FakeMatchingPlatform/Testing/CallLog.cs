using System.Collections.Concurrent;

namespace FakeMatchingPlatform.Testing;

public sealed record CallCount(string Method, string Path, string? CandidateId, int StatusCode, int Count);

/// <summary>
/// Counts PUT on <c>/profiles/{candidateId}</c>, so tests can prove "one effect".
/// Never reset: every scenario uses its own candidate id.
/// </summary>
public sealed class CallLog
{
    private readonly ConcurrentDictionary<(string Method, string Path, string? CandidateId, int StatusCode), int> counts = new();

    public void Record(string method, string path, string? candidateId, int statusCode) =>
        counts.AddOrUpdate((method, path, candidateId, statusCode), 1, (_, count) => count + 1);

    public IEnumerable<CallCount> Snapshot(string? candidateId) =>
        counts
            .Where(entry => candidateId is null || entry.Key.CandidateId == candidateId)
            .Select(entry => new CallCount(entry.Key.Method, entry.Key.Path, entry.Key.CandidateId, entry.Key.StatusCode, entry.Value))
            .OrderBy(call => call.Path)
            .ThenBy(call => call.Method)
            .ThenBy(call => call.StatusCode);
}

public static class CallLogEndpoints
{
    // Counted after the response is produced, so anomaly 503 and abandoned timeouts (499) are included.
    public static void UseCallLog(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            await next(context);

            if (!context.Request.RouteValues.TryGetValue("candidateId", out var value) || value is not string candidateId
                || !context.Request.Path.StartsWithSegments("/profiles")
                || !HttpMethods.IsPut(context.Request.Method))
            {
                return;
            }

            context.RequestServices.GetRequiredService<CallLog>().Record(
                context.Request.Method, context.Request.Path.Value!, candidateId, context.Response.StatusCode);
        });

    public static void MapCalls(this IEndpointRouteBuilder app) =>
        app.MapGet("/calls", (string? candidateId, CallLog calls) => calls.Snapshot(candidateId));
}
