namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

/// <summary>Reads the ids out of a raw webhook body. Does not verify the signature.</summary>
public interface IWebhookParser
{
    WebhookParseResult Parse(ReadOnlySpan<byte> rawBody);
}
