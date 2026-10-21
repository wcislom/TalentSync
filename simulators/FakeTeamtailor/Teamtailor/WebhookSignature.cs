using System.Security.Cryptography;
using System.Text;

namespace FakeTeamtailor.Teamtailor;

/// <summary>
/// <c>TT-Signature</c> v2 as documented by Teamtailor, written separately from the verifier in <c>src/</c>.
/// Public for integration tests that call Ingress directly.
/// </summary>
public static class WebhookSignature
{
    /// <summary>Base64 of <c>t=&lt;unix seconds&gt;,v2=&lt;hex HMAC-SHA256 of "t." + body&gt;</c>.</summary>
    public static string Sign(string key, byte[] body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signed = Encoding.ASCII.GetBytes(timestamp + ".").Concat(body).ToArray();
        var v2 = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), signed));
        return Convert.ToBase64String(Encoding.ASCII.GetBytes($"t={timestamp},v2={v2}"));
    }
}
