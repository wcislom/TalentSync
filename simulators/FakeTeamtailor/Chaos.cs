using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace FakeTeamtailor;

public sealed record ChaosRequest(string Mode, int? Seconds, string? CandidateId, int? Count, int? DelayMs);

public sealed record ChaosRule(string Mode, string? CandidateId, DateTimeOffset? Until, int Count, int DelayMs);

/// <summary>
/// Active failure modes. Several can run at once (parallel tests, each with its own candidate);
/// a rule without a candidate applies to everyone.
/// </summary>
public sealed class ChaosRules(TimeProvider timeProvider)
{
    public const string RateLimit = "rate-limit";
    public const string BadRequest = "bad-request";
    public const string Delay = "delay";
    public const string DuplicateWebhook = "duplicate-webhook";

    public static readonly string[] Modes = [RateLimit, BadRequest, Delay, DuplicateWebhook];

    private readonly ConcurrentDictionary<(string Mode, string? CandidateId), ChaosRule> rules = new();

    public DateTimeOffset Now => timeProvider.GetUtcNow();

    public void Set(ChaosRule rule) => rules[(rule.Mode, rule.CandidateId)] = rule;

    public void Clear(string? candidateId)
    {
        foreach (var key in rules.Keys.Where(key => candidateId is null || key.CandidateId == candidateId))
        {
            rules.TryRemove(key, out _);
        }
    }

    public IEnumerable<ChaosRule> Active() => rules.Values.Where(rule => rule.Until is null || rule.Until > Now);

    public ChaosRule? Find(string mode, string? candidateId) =>
        Active().FirstOrDefault(rule => rule.Mode == mode && (rule.CandidateId is null || rule.CandidateId == candidateId));
}

public static class ChaosEndpoints
{
    public static void MapChaos(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chaos", (ChaosRequest request, ChaosRules chaos) =>
        {
            if (!ChaosRules.Modes.Contains(request.Mode))
            {
                return Results.BadRequest($"Unknown mode {request.Mode}; known: {string.Join(", ", ChaosRules.Modes)}");
            }

            // X-Rate-Limit-Reset is computed from the end of the rule, so it needs one.
            if (request.Mode == ChaosRules.RateLimit && request.Seconds is null)
            {
                return Results.BadRequest("rate-limit needs seconds");
            }

            if (request.Mode == ChaosRules.Delay && request.DelayMs is null)
            {
                return Results.BadRequest("delay needs delayMs");
            }

            var until = request.Seconds is { } seconds ? chaos.Now.AddSeconds(seconds) : (DateTimeOffset?)null;
            chaos.Set(new ChaosRule(request.Mode, request.CandidateId, until, request.Count ?? 2, request.DelayMs ?? 0));
            return Results.Ok(chaos.Active());
        });

        app.MapGet("/chaos", (ChaosRules chaos) => chaos.Active());

        app.MapDelete("/chaos", (string? candidateId, ChaosRules chaos) =>
        {
            chaos.Clear(candidateId);
            return Results.Ok(chaos.Active());
        });
    }

    /// <summary>Runs after the API header check, so a wrong key still answers 401.</summary>
    public static async ValueTask<object?> ApplyToApi(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var chaos = http.RequestServices.GetRequiredService<ChaosRules>();
        var candidateId = http.RequestServices.GetRequiredService<Store>().CandidateIdOf(http.Request);

        if (chaos.Find(ChaosRules.Delay, candidateId) is { } delay)
        {
            await Task.Delay(delay.DelayMs, http.RequestAborted);
        }

        if (chaos.Find(ChaosRules.RateLimit, candidateId) is { Until: { } until })
        {
            // Rounded up: a client that waits exactly this long must not come back before the mode ends
            // (scenario S5 expects exactly one 429 and one successful call).
            var reset = (int)Math.Ceiling((until - chaos.Now).TotalSeconds);
            http.Response.Headers["X-Rate-Limit-Limit"] = "50";
            http.Response.Headers["X-Rate-Limit-Remaining"] = "0";
            http.Response.Headers["X-Rate-Limit-Reset"] = reset.ToString();
            return Error(StatusCodes.Status429TooManyRequests, "Rate limit exceeded");
        }

        if (chaos.Find(ChaosRules.BadRequest, candidateId) is not null)
        {
            return Error(StatusCodes.Status400BadRequest, "Bad request (chaos)");
        }

        return await next(context);
    }

    private static IResult Error(int status, string title) =>
        Results.Json(
            new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["status"] = status.ToString(), ["title"] = title }) },
            contentType: "application/vnd.api+json",
            statusCode: status);
}
