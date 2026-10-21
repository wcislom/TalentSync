namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

/// <summary>What the webhook parser reads from the <c>Teamtailor</c> configuration section.</summary>
internal sealed class TeamtailorWebhookOptions
{
    public const string SectionName = "Teamtailor";

    /// <summary>The webhook's signature key from Teamtailor. A secret: user-secrets or environment only.</summary>
    public string WebhookSignatureKey { get; set; } = "";

    /// <summary>
    /// How far the signed timestamp may be from our clock, either way. Teamtailor documents no window; a wider one
    /// accepts more clock skew and keeps a captured webhook replayable for longer.
    /// </summary>
    public TimeSpan WebhookSignatureTolerance { get; set; }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(WebhookSignatureKey)
        && WebhookSignatureTolerance > TimeSpan.Zero;
}
