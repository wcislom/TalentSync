using System.Collections.Concurrent;

namespace FakeCrm.Testing;

public sealed record AnomalyRequest(string Mode, int? Seconds, string? CandidateId);

public sealed record AnomalyRule(string Mode, string? CandidateId, DateTimeOffset? Until);

/// <summary>
/// Active failure modes. Several can run at once (parallel tests, each with its own candidate);
/// a rule without a candidate applies to everyone.
/// </summary>
public sealed class AnomalyRules(TimeProvider timeProvider)
{
    public const string ServiceUnavailable = "503";
    public const string Timeout = "timeout";

    public static readonly string[] Modes = [ServiceUnavailable, Timeout];

    private readonly ConcurrentDictionary<(string Mode, string? CandidateId), AnomalyRule> rules = new();

    public DateTimeOffset Now => timeProvider.GetUtcNow();

    public void Set(AnomalyRule rule) => rules[(rule.Mode, rule.CandidateId)] = rule;

    public void Clear(string? candidateId)
    {
        foreach (var key in rules.Keys.Where(key => candidateId is null || key.CandidateId == candidateId))
        {
            rules.TryRemove(key, out _);
        }
    }

    public IEnumerable<AnomalyRule> Active() => rules.Values.Where(rule => rule.Until is null || rule.Until > Now);

    public AnomalyRule? Find(string mode, string? candidateId) =>
        Active().FirstOrDefault(rule => rule.Mode == mode && (rule.CandidateId is null || rule.CandidateId == candidateId));
}

public static class AnomalyEndpoints
{
    public static void MapAnomalies(this IEndpointRouteBuilder app)
    {
        app.MapPost("/anomalies", (AnomalyRequest request, AnomalyRules anomalies) =>
        {
            if (!AnomalyRules.Modes.Contains(request.Mode))
            {
                return Results.BadRequest($"Unknown mode {request.Mode}; known: {string.Join(", ", AnomalyRules.Modes)}");
            }

            var until = request.Seconds is { } seconds ? anomalies.Now.AddSeconds(seconds) : (DateTimeOffset?)null;
            anomalies.Set(new AnomalyRule(request.Mode, request.CandidateId, until));
            return Results.Ok(anomalies.Active());
        });

        app.MapGet("/anomalies", (AnomalyRules anomalies) => anomalies.Active());

        app.MapDelete("/anomalies", (string? candidateId, AnomalyRules anomalies) =>
        {
            anomalies.Clear(candidateId);
            return Results.Ok(anomalies.Active());
        });
    }

    public static async ValueTask<object?> ApplyToApi(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var anomalies = http.RequestServices.GetRequiredService<AnomalyRules>();
        var candidateId = http.Request.RouteValues["candidateId"] as string;

        if (anomalies.Find(AnomalyRules.ServiceUnavailable, candidateId) is not null)
        {
            return Results.Problem("Service unavailable (anomaly)", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (anomalies.Find(AnomalyRules.Timeout, candidateId) is null)
        {
            return await next(context);
        }

        // The effect happens, then the response never comes: the client cannot tell this from a request that
        // never arrived, and its retry repeats an effect that already happened. The upsert keyed by candidate id
        // makes that harmless (invariant 2). The rule may expire during the wait; the request still hangs.
        var result = await next(context);
        try
        {
            await Task.Delay(System.Threading.Timeout.Infinite, http.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            http.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return Results.Empty;
        }

        return result;
    }
}
