using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FakeTeamtailor.State;

namespace FakeTeamtailor.Testing;

public sealed record AnomalyRequest(string Mode, int? Seconds, string? CandidateId, int? Count, int? DelayMs);

public sealed record AnomalyRule(string Mode, string? CandidateId, DateTimeOffset? Until, int Count, int DelayMs);

/// <summary>
/// Active failure modes. Several can run at once (parallel tests, each with its own candidate);
/// a rule without a candidate applies to everyone.
/// </summary>
public sealed class AnomalyRules(TimeProvider timeProvider)
{
    public const string RateLimit = "rate-limit";
    public const string BadRequest = "bad-request";
    public const string Delay = "delay";
    public const string DuplicateWebhook = "duplicate-webhook";

    public static readonly string[] Modes = [RateLimit, BadRequest, Delay, DuplicateWebhook];

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

            // X-Rate-Limit-Reset is computed from the end of the rule, so it needs one.
            if (request.Mode == AnomalyRules.RateLimit && request.Seconds is null)
            {
                return Results.BadRequest("rate-limit needs seconds");
            }

            if (request.Mode == AnomalyRules.Delay && request.DelayMs is null)
            {
                return Results.BadRequest("delay needs delayMs");
            }

            var until = request.Seconds is { } seconds ? anomalies.Now.AddSeconds(seconds) : (DateTimeOffset?)null;
            anomalies.Set(new AnomalyRule(request.Mode, request.CandidateId, until, request.Count ?? 2, request.DelayMs ?? 0));
            return Results.Ok(anomalies.Active());
        });

        app.MapGet("/anomalies", (AnomalyRules anomalies) => anomalies.Active());

        app.MapDelete("/anomalies", (string? candidateId, AnomalyRules anomalies) =>
        {
            anomalies.Clear(candidateId);
            return Results.Ok(anomalies.Active());
        });
    }

    /// <summary>Runs after the API header check, so a wrong key still answers 401.</summary>
    public static async ValueTask<object?> ApplyToApi(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var anomalies = http.RequestServices.GetRequiredService<AnomalyRules>();
        var candidateId = http.RequestServices.GetRequiredService<Store>().CandidateIdOf(http.Request);

        if (anomalies.Find(AnomalyRules.Delay, candidateId) is { } delay)
        {
            await Task.Delay(delay.DelayMs, http.RequestAborted);
        }

        if (anomalies.Find(AnomalyRules.RateLimit, candidateId) is { Until: { } until })
        {
            // Rounded up: a client that waits exactly this long must not come back before the mode ends
            // (scenario S5 expects exactly one 429 and one successful call).
            var reset = (int)Math.Ceiling((until - anomalies.Now).TotalSeconds);
            http.Response.Headers["X-Rate-Limit-Limit"] = "50";
            http.Response.Headers["X-Rate-Limit-Remaining"] = "0";
            http.Response.Headers["X-Rate-Limit-Reset"] = reset.ToString();
            return Error(StatusCodes.Status429TooManyRequests, "Rate limit exceeded");
        }

        if (anomalies.Find(AnomalyRules.BadRequest, candidateId) is not null)
        {
            return Error(StatusCodes.Status400BadRequest, "Bad request (anomaly)");
        }

        return await next(context);
    }

    private static IResult Error(int status, string title) =>
        Results.Json(
            new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["status"] = status.ToString(), ["title"] = title }) },
            contentType: "application/vnd.api+json",
            statusCode: status);
}
