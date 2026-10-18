using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TalentSync.Infrastructure.Teamtailor.Webhooks;

namespace TalentSync.Infrastructure.Teamtailor.Tests;

public class TeamtailorWebhookSignatureTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""
        { "payload": { "event_name": "candidate.create", "data": { "id": "c-1", "type": "candidates" } } }
        """);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly TeamtailorWebhookParser _parser;

    public TeamtailorWebhookSignatureTests()
    {
        _parser = new(
            Options.Create(new TeamtailorWebhookOptions { WebhookSignatureKey = WebhookSigner.Key, WebhookSignatureTolerance = Tolerance }),
            _time);
    }

    [Fact]
    public void ValidSignature_IsParsed()
    {
        Assert.IsType<WebhookParseResult.Parsed>(_parser.Parse(WebhookSigner.Sign(Body, _time.GetUtcNow()), Body));
    }

    [Fact]
    public void UppercaseHex_IsAccepted()
    {
        var timestamp = _time.GetUtcNow().ToUnixTimeSeconds().ToString();
        var header = WebhookSigner.Header(timestamp, WebhookSigner.Hmac(WebhookSigner.Key, timestamp, Body).ToUpperInvariant());

        Assert.IsType<WebhookParseResult.Parsed>(_parser.Parse(header, Body));
    }

    [Fact]
    public void UnknownParts_AreIgnored()
    {
        var timestamp = _time.GetUtcNow().ToUnixTimeSeconds().ToString();
        var v2 = WebhookSigner.Hmac(WebhookSigner.Key, timestamp, Body);
        var header = Convert.ToBase64String(Encoding.ASCII.GetBytes($"t={timestamp},v1=abc,v2={v2}"));

        Assert.IsType<WebhookParseResult.Parsed>(_parser.Parse(header, Body));
    }

    [Fact]
    public void WrongKey_IsUnauthenticated()
    {
        AssertUnauthenticated(WebhookSigner.Sign(Body, _time.GetUtcNow(), key: "another-key"), Body, "signature mismatch");
    }

    [Fact]
    public void BodyChangedAfterSigning_IsUnauthenticated()
    {
        var header = WebhookSigner.Sign(Body, _time.GetUtcNow());
        var tampered = Body.ToArray();
        tampered[^3] ^= 1;

        AssertUnauthenticated(header, tampered, "signature mismatch");
    }

    [Fact]
    public void TimestampChangedAfterSigning_IsUnauthenticated()
    {
        var signed = _time.GetUtcNow().ToUnixTimeSeconds().ToString();
        var header = WebhookSigner.Header((long.Parse(signed) + 1).ToString(), WebhookSigner.Hmac(WebhookSigner.Key, signed, Body));

        AssertUnauthenticated(header, Body, "signature mismatch");
    }

    [Theory]
    [InlineData(null, "missing TT-Signature header")]
    [InlineData("", "missing TT-Signature header")]
    [InlineData("not base64!", "TT-Signature is not Base64")]
    public void MissingOrUnreadableHeader_IsUnauthenticated(string? header, string expectedReason)
    {
        AssertUnauthenticated(header, Body, expectedReason);
    }

    [Theory]
    [InlineData("v2=00ff", "TT-Signature has no valid t")]
    [InlineData("t=-5,v2=00ff", "TT-Signature has no valid t")]
    [InlineData("t=1792324800", "TT-Signature has no v2")]
    [InlineData("t=1792324800,v2=xyz", "TT-Signature v2 is not hex")]
    public void MalformedHeader_IsUnauthenticated(string decodedHeader, string expectedReason)
    {
        AssertUnauthenticated(Convert.ToBase64String(Encoding.ASCII.GetBytes(decodedHeader)), Body, expectedReason);
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(300)]
    public void TimestampAtTolerance_IsAccepted(int offsetSeconds)
    {
        var header = WebhookSigner.Sign(Body, _time.GetUtcNow().AddSeconds(offsetSeconds));

        Assert.IsType<WebhookParseResult.Parsed>(_parser.Parse(header, Body));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void TimestampBeyondTolerance_IsUnauthenticated(int offsetSeconds)
    {
        AssertUnauthenticated(WebhookSigner.Sign(Body, _time.GetUtcNow().AddSeconds(offsetSeconds)), Body, "timestamp outside tolerance");
    }

    [Fact]
    public void ValidWebhook_ExpiresWhenTheClockMovesPastTolerance()
    {
        var header = WebhookSigner.Sign(Body, _time.GetUtcNow());
        _time.Advance(Tolerance + TimeSpan.FromSeconds(1));

        AssertUnauthenticated(header, Body, "timestamp outside tolerance");
    }

    private void AssertUnauthenticated(string? header, byte[] body, string expectedReason)
    {
        var unauthenticated = Assert.IsType<WebhookParseResult.Unauthenticated>(_parser.Parse(header, body));
        Assert.Equal(expectedReason, unauthenticated.Reason);
    }
}
