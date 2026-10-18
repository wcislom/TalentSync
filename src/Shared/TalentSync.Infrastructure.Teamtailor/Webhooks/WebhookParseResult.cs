namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

/// <summary>Outcome of parsing a webhook body.</summary>
public abstract record WebhookParseResult
{
    private WebhookParseResult()
    {
    }

    public sealed record Parsed(WebhookNotification Notification) : WebhookParseResult;

    /// <summary>A well-formed webhook for an event no worker handles (e.g. <c>job.*</c>). Acknowledge and drop.</summary>
    public sealed record Ignored(string EventName) : WebhookParseResult;

    /// <summary>
    /// The body is not a webhook we can read. <paramref name="Reason"/> names the missing or malformed field only,
    /// never the body (invariant 14).
    /// </summary>
    public sealed record Invalid(string Reason) : WebhookParseResult;

    /// <summary>
    /// The signature is missing, wrong or outside the time window: the body is not read. <paramref name="Reason"/>
    /// names the failed check only.
    /// </summary>
    public sealed record Unauthenticated(string Reason) : WebhookParseResult;
}
