using System.Security.Cryptography;
using TalentSync.Contracts;
using TalentSync.Infrastructure.Teamtailor.Webhooks;
using TalentSync.Ingress.Api.Outbox;
using TalentSync.Ingress.Api.Persistence;

namespace TalentSync.Ingress.Api.Webhooks;

/// <summary>
/// Receives a webhook, stores it as an outbox row and acknowledges. No API calls, no stage interpretation:
/// the relay publishes the row and the workers fetch the current state (notify-then-fetch).
/// </summary>
internal static class TeamtailorWebhookEndpoint
{
    /// <summary>Value of <see cref="EventEnvelope.Source"/>; part of the consumers' inbox key.</summary>
    private const string Source = "teamtailor";

    public static IEndpointRouteBuilder MapTeamtailorWebhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/teamtailor", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        IWebhookParser parser,
        IngressDbContext db,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(TeamtailorWebhookEndpoint));

        // Raw bytes, read once: the event id (and later signature v2) is computed over the body as sent,
        // never over re-serialised JSON.
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        var rawBody = buffer.ToArray();

        // TODO(MVP, last step): verify TT-Signature v2 here and return 401 when it is invalid.

        switch (parser.Parse(rawBody))
        {
            case WebhookParseResult.Parsed parsed:
                return await StoreAsync(parsed.Notification, rawBody, db, timeProvider, logger, cancellationToken);

            case WebhookParseResult.Ignored ignored:
                // No consumer for this event (e.g. job.*): acknowledge so the sender does not treat it as a failure.
                logger.LogDebug("Ignored webhook {EventName}", ignored.EventName);
                return Results.Ok();

            case WebhookParseResult.Invalid invalid:
                // Not routable (no partition key) or not a webhook. Teamtailor does not retry, so the event is lost;
                // the log is the only trace. Reason names a field only, never the body (invariant 14).
                logger.LogWarning("Rejected unreadable webhook: {Reason}", invalid.Reason);
                return Results.BadRequest();

            default:
                throw new InvalidOperationException("Unknown webhook parse result.");
        }
    }

    private static async Task<IResult> StoreAsync(
        WebhookNotification notification,
        byte[] rawBody,
        IngressDbContext db,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var eventId = Convert.ToHexStringLower(SHA256.HashData(rawBody));
        var receivedAt = timeProvider.GetUtcNow();

        var envelope = new EventEnvelope
        {
            EventId = eventId,
            Source = Source,
            Type = notification.EventName,
            SchemaVersion = EventEnvelope.CurrentSchemaVersion,
            ResourceType = notification.ResourceType,
            ResourceId = notification.ResourceId,
            CandidateId = notification.CandidateId,
            ReceivedAt = receivedAt,
        };

        db.Outbox.Add(new OutboxMessage
        {
            EventId = eventId,
            CandidateId = notification.CandidateId,
            Type = notification.EventName,
            Envelope = EventEnvelopeJson.Serialize(envelope),
            CreatedAt = receivedAt.UtcDateTime,
        });

        try
        {
            // A single insert: SaveChanges runs it in its own transaction. A redelivered webhook is stored again
            // with the same EventId; consumers skip it through their inbox (invariant 1).
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Known limitation (ADR 004): Teamtailor may not retry a 503, so this event is lost.
            // The exception carries no PII: the row holds ids only and sensitive data logging is off.
            logger.LogError(exception, "Could not store webhook {EventId} ({EventName}) in the outbox", eventId, notification.EventName);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        logger.LogInformation("Stored webhook {EventId} ({EventName}) for candidate {CandidateId}", eventId, notification.EventName, notification.CandidateId);
        return Results.Ok();
    }
}
