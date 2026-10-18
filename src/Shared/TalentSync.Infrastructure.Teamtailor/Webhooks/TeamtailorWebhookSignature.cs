using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

/// <summary>
/// Signature v2 of the <c>TT-Signature</c> header: Base64 of <c>t=&lt;unix seconds&gt;,v2=&lt;hex&gt;</c>, where the hex is
/// HMAC-SHA256 over <c>t + "." + raw body</c>. Details and sources: <c>.claude/rules/teamtailor.md</c>.
/// </summary>
internal static class TeamtailorWebhookSignature
{
    /// <param name="key">The signature key as UTF-8 bytes. ASSUMPTION: Teamtailor does not document the key's encoding.</param>
    /// <param name="reason">Names the failed check only, never the header or the body (invariant 14).</param>
    public static bool Verify(
        string? header,
        ReadOnlySpan<byte> body,
        byte[] key,
        TimeSpan tolerance,
        DateTimeOffset now,
        out string reason)
    {
        if (string.IsNullOrEmpty(header))
        {
            reason = "missing TT-Signature header";
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.ASCII.GetString(Convert.FromBase64String(header));
        }
        catch (FormatException)
        {
            reason = "TT-Signature is not Base64";
            return false;
        }

        string? timestamp = null;
        string? signature = null;
        foreach (var part in decoded.Split(',', StringSplitOptions.TrimEntries))
        {
            // Unknown parts (v1, a future v3) are ignored: only v2 signs the whole body.
            var separator = part.IndexOf('=');
            switch (separator > 0 ? part[..separator] : "")
            {
                case "t":
                    timestamp = part[(separator + 1)..];
                    break;
                case "v2":
                    signature = part[(separator + 1)..];
                    break;
            }
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var signedAt))
        {
            reason = "TT-Signature has no valid t";
            return false;
        }

        byte[] actual;
        try
        {
            actual = Convert.FromHexString(signature ?? "");
        }
        catch (FormatException)
        {
            reason = "TT-Signature v2 is not hex";
            return false;
        }

        if (actual.Length == 0)
        {
            reason = "TT-Signature has no v2";
            return false;
        }

        // The timestamp is hashed as sent: re-formatting the parsed number could change its bytes.
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData(Encoding.ASCII.GetBytes(timestamp!));
        hmac.AppendData("."u8);
        hmac.AppendData(body);
        if (!CryptographicOperations.FixedTimeEquals(hmac.GetHashAndReset(), actual))
        {
            reason = "signature mismatch";
            return false;
        }

        // Checked after the HMAC, so this reason means a genuine but late or clock-skewed webhook, not a forgery.
        // A replay inside the window gets through; it has the same bytes, so the same EventId, and consumers skip it.
        if (Math.Abs(now.ToUnixTimeSeconds() - signedAt) > tolerance.TotalSeconds)
        {
            reason = "timestamp outside tolerance";
            return false;
        }

        reason = "";
        return true;
    }
}
