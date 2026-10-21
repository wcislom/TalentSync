using System.Text.Json;

namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

/// <summary>
/// Tolerant reader of <c>{ "payload": { "event_name", "data": { "id", "type", "relationships" } }, "signature" }</c>.
/// Unknown fields are ignored; attributes are never read.
/// </summary>
internal sealed class TeamtailorWebhookParser : IWebhookParser
{
    private const string CandidatePrefix = "candidate.";
    private const string JobApplicationPrefix = "job_application.";

    public WebhookParseResult Parse(ReadOnlySpan<byte> rawBody)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody.ToArray());
        }
        catch (JsonException)
        {
            return new WebhookParseResult.Invalid("malformed JSON");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return new WebhookParseResult.Invalid("missing payload");
            }

            if (!TryGetString(payload, "event_name", out var eventName))
            {
                return new WebhookParseResult.Invalid("missing payload.event_name");
            }

            var isCandidateEvent = eventName.StartsWith(CandidatePrefix, StringComparison.Ordinal);
            var isJobApplicationEvent = eventName.StartsWith(JobApplicationPrefix, StringComparison.Ordinal);
            if (!isCandidateEvent && !isJobApplicationEvent)
            {
                // job.* and anything new: no candidate, so no partition key and no consumer.
                return new WebhookParseResult.Ignored(eventName);
            }

            if (!payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return new WebhookParseResult.Invalid("missing payload.data");
            }

            if (!TryGetId(data, out var resourceId))
            {
                return new WebhookParseResult.Invalid("missing payload.data.id");
            }

            if (!TryGetString(data, "type", out var resourceType))
            {
                return new WebhookParseResult.Invalid("missing payload.data.type");
            }

            string candidateId;
            if (isCandidateEvent)
            {
                candidateId = resourceId;
            }
            else if (!TryGetRelatedCandidateId(data, out candidateId))
            {
                // ASSUMPTION (teamtailor rule): job application webhooks carry relationships.candidate.
                // Not documented, and destroy events carry a reduced data object. Without it there is no
                // partition key, so the event cannot be routed: reject it rather than guess.
                return new WebhookParseResult.Invalid("missing payload.data.relationships.candidate.data.id");
            }

            return new WebhookParseResult.Parsed(new WebhookNotification(eventName, resourceType, resourceId, candidateId));
        }
    }

    private static bool TryGetRelatedCandidateId(JsonElement data, out string candidateId)
    {
        candidateId = "";
        return data.TryGetProperty("relationships", out var relationships)
            && relationships.ValueKind == JsonValueKind.Object
            && relationships.TryGetProperty("candidate", out var candidate)
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty("data", out var candidateData)
            && candidateData.ValueKind == JsonValueKind.Object
            && TryGetId(candidateData, out candidateId);
    }

    /// <summary>
    /// JSON:API ids are strings. A number is accepted too, because the webhook docs do not show the type
    /// of <c>data.id</c>.
    /// </summary>
    private static bool TryGetId(JsonElement element, out string id)
    {
        id = "";
        if (!element.TryGetProperty("id", out var value))
        {
            return false;
        }

        id = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => "",
        };
        return id.Length > 0;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? "";
        }

        return value.Length > 0;
    }
}
