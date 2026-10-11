using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FakeTeamtailor;

/// <summary>Outcome of one delivery; <c>StatusCode</c> is null when Ingress could not be reached.</summary>
public sealed record WebhookDelivery(string EventName, int? StatusCode);

/// <summary>
/// Posts <c>{ "payload": { "event_name", "data" } }</c> to Ingress; <c>data</c> comes from <see cref="TeamtailorJson"/>.
/// Not signed yet: signature v2 comes last, together with the verifier in Ingress.
/// </summary>
public sealed class WebhookSender(HttpClient http, IConfiguration configuration, ILogger<WebhookSender> logger)
{
    private readonly string webhookUrl = configuration["FakeTeamtailor:WebhookUrl"]
        ?? throw new InvalidOperationException("FakeTeamtailor:WebhookUrl is not configured.");

    public async Task<WebhookDelivery> SendAsync(string eventName, JsonObject data, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["payload"] = new JsonObject { ["event_name"] = eventName, ["data"] = data },
        };

        // Bytes, not a string: Ingress derives EventId from the raw body, so a duplicate must be byte-identical.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        var resourceId = data["id"]?.GetValue<string>();

        try
        {
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new(MediaTypeNames.Application.Json);
            using var response = await http.PostAsync(webhookUrl, content, cancellationToken);

            logger.LogInformation("Webhook {EventName} for {ResourceId}: {StatusCode}", eventName, resourceId, (int)response.StatusCode);
            return new(eventName, (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            // Teamtailor does not retry a failed delivery (changelog 2026-03-19), so neither does the fake.
            logger.LogWarning("Webhook {EventName} for {ResourceId} not delivered: {Error}", eventName, resourceId, exception.Message);
            return new(eventName, null);
        }
    }
}
