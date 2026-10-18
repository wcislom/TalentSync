using System.Security.Cryptography;
using System.Text;

namespace TalentSync.Infrastructure.Teamtailor.Tests;

/// <summary>
/// Signs like Teamtailor does (v2), written from the documentation and not from the code under test,
/// so a shared misreading cannot make both sides agree.
/// </summary>
internal static class WebhookSigner
{
    public const string Key = "test-webhook-signature-key";

    public static string Sign(byte[] body, DateTimeOffset signedAt, string key = Key) =>
        Header(signedAt.ToUnixTimeSeconds().ToString(), Hmac(key, signedAt.ToUnixTimeSeconds().ToString(), body));

    public static string Header(string timestamp, string v2) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"t={timestamp},v2={v2}"));

    public static string Hmac(string key, string timestamp, byte[] body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes(timestamp + ".").Concat(body).ToArray()));
}
