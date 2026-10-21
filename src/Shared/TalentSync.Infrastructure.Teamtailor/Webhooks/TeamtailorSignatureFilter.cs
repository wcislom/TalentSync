using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

/// <summary>
/// Signature v2 of the <c>TT-Signature</c> header: Base64 of <c>t=&lt;unix seconds&gt;,v2=&lt;hex&gt;</c>, where the hex is
/// HMAC-SHA256 over <c>t + "." + raw body</c>. Details and sources: <c>.claude/rules/teamtailor.md</c>.
/// </summary>
internal sealed class TeamtailorSignatureFilter(
    IOptions<TeamtailorWebhookOptions> options,
    TimeProvider timeProvider,
    ILogger<TeamtailorSignatureFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;

        // The endpoint reads the body again: it parses it and derives the event id from the same bytes.
        request.EnableBuffering();
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, context.HttpContext.RequestAborted);
        request.Body.Position = 0;

        var failure = Check(request.Headers["TT-Signature"], body.ToArray());
        if (failure is not null)
        {
            logger.LogWarning("Rejected webhook: {Reason}", failure);
            return Results.Unauthorized();
        }

        return await next(context);
    }

    /// <returns>Null when valid, otherwise the failed check: never the header or the body (invariant 14).</returns>
    internal string? Check(string? header, byte[] body)
    {
        string decoded;
        try
        {
            decoded = Encoding.ASCII.GetString(Convert.FromBase64String(header ?? ""));
        }
        catch (FormatException)
        {
            return "malformed TT-Signature";
        }

        // Unknown parts (v1, a future v3) are ignored: only v2 signs the whole body.
        var parts = decoded.Split(',', StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToLookup(pair => pair[0], pair => pair[1]);
        var timestamp = parts["t"].FirstOrDefault();
        var signature = parts["v2"].FirstOrDefault();
        if (signature is null || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var signedAt))
        {
            return "malformed TT-Signature";
        }

        // ASSUMPTION: the key is used as its UTF-8 bytes; Teamtailor does not document its encoding.
        // The timestamp is hashed as sent: re-formatting the parsed number could change its bytes.
        var key = Encoding.UTF8.GetBytes(options.Value.WebhookSignatureKey);
        byte[] signed = [.. Encoding.ASCII.GetBytes(timestamp + "."), .. body];
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(key, signed));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.ToLowerInvariant())))
        {
            return "signature mismatch";
        }

        // Checked after the HMAC, so this reason means a genuine but late or clock-skewed webhook, not a forgery.
        // A replay inside the window gets through; it has the same bytes, so the same EventId, and consumers skip it.
        if (Math.Abs(timeProvider.GetUtcNow().ToUnixTimeSeconds() - signedAt) > options.Value.WebhookSignatureTolerance.TotalSeconds)
        {
            return "timestamp outside tolerance";
        }

        return null;
    }
}
