namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>One row of a simulator's <c>GET /calls</c>: how many requests got this status code.</summary>
public sealed record SimulatorCall(string Method, string Path, string? CandidateId, int StatusCode, int Count);

public static class SimulatorCallExtensions
{
    /// <summary>Requests to one method and path, whatever their status code.</summary>
    public static int CountOf(this IEnumerable<SimulatorCall> calls, string method, string path) =>
        calls.Where(call => call.Method == method && call.Path == path).Sum(call => call.Count);
}
