using System.Collections.Concurrent;
using FakeTeamtailor.State;

namespace FakeTeamtailor.Testing;

public sealed record CallCount(string Method, string Path, string? CandidateId, int StatusCode, int Count);

/// <summary>
/// Counts requests to <c>/v1</c>, so tests can prove "one call" (or "two calls, one of them 429").
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
            .ThenBy(call => call.StatusCode);
}

public static class CallLogEndpoints
{
    // Counted after the response is produced, so 401 from the header filter (and later anomaly 429/400) is included.
    public static void UseCallLog(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            await next(context);

            if (!context.Request.Path.StartsWithSegments("/v1"))
            {
                return;
            }

            var services = context.RequestServices;
            var candidateId = services.GetRequiredService<Store>().CandidateIdOf(context.Request);
            services.GetRequiredService<CallLog>().Record(
                context.Request.Method, context.Request.Path.Value!, candidateId, context.Response.StatusCode);
        });

    public static void MapCalls(this IEndpointRouteBuilder app) =>
        app.MapGet("/calls", (string? candidateId, CallLog calls) => calls.Snapshot(candidateId));
}
