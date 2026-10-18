namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

/// <summary>Verifies a webhook's signature, then reads the ids out of its raw body.</summary>
public interface IWebhookParser
{
    /// <param name="signatureHeader">The <c>TT-Signature</c> header as received; null when it is missing.</param>
    /// <param name="rawBody">The body as received: the signature covers these bytes, not re-serialised JSON.</param>
    WebhookParseResult Parse(string? signatureHeader, ReadOnlySpan<byte> rawBody);
}
