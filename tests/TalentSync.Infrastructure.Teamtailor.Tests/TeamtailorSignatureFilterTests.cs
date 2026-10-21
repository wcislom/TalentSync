using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TalentSync.Infrastructure.Teamtailor.Webhooks;

namespace TalentSync.Infrastructure.Teamtailor.Tests;

public class TeamtailorSignatureFilterTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""
        { "payload": { "event_name": "candidate.create", "data": { "id": "c-1", "type": "candidates" } } }
        """);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly TeamtailorSignatureFilter _filter;

    public TeamtailorSignatureFilterTests()
    {
        _filter = new(
            Options.Create(new TeamtailorWebhookOptions { WebhookSignatureKey = WebhookSigner.Key, WebhookSignatureTolerance = Tolerance }),
            _time,
            NullLogger<TeamtailorSignatureFilter>.Instance);
    }

    [Fact]
    public async Task ValidSignature_RunsTheHandlerWithTheBodyRewound()
    {
        var context = Request(WebhookSigner.Sign(Body, _time.GetUtcNow()));
        byte[]? bodySeenByHandler = null;

        var result = await _filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(context), async invocation =>
        {
            using var copy = new MemoryStream();
            await invocation.HttpContext.Request.Body.CopyToAsync(copy);
            bodySeenByHandler = copy.ToArray();
            return Results.Ok();
        });

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok>(result);
        Assert.Equal(Body, bodySeenByHandler);
    }

    [Fact]
    public async Task InvalidSignature_Returns401WithoutRunningTheHandler()
    {
        var handlerRan = false;

        var result = await _filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(Request(null)), _ =>
        {
            handlerRan = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(result);
        Assert.False(handlerRan);
    }

    [Fact]
    public void UppercaseHex_IsAccepted()
    {
        var timestamp = _time.GetUtcNow().ToUnixTimeSeconds().ToString();
        var header = WebhookSigner.Header(timestamp, WebhookSigner.Hmac(WebhookSigner.Key, timestamp, Body).ToUpperInvariant());

        Assert.Null(_filter.Check(header, Body));
    }

    [Fact]
    public void UnknownParts_AreIgnored()
    {
        var timestamp = _time.GetUtcNow().ToUnixTimeSeconds().ToString();
        var v2 = WebhookSigner.Hmac(WebhookSigner.Key, timestamp, Body);
        var header = Convert.ToBase64String(Encoding.ASCII.GetBytes($"t={timestamp},v1=abc,v2={v2}"));

        Assert.Null(_filter.Check(header, Body));
    }

    [Fact]
    public void WrongKey_IsAMismatch()
    {
        Assert.Equal("signature mismatch", _filter.Check(WebhookSigner.Sign(Body, _time.GetUtcNow(), key: "another-key"), Body));
    }

    [Fact]
    public void BodyChangedAfterSigning_IsAMismatch()
    {
        var header = WebhookSigner.Sign(Body, _time.GetUtcNow());
        var tampered = Body.ToArray();
        tampered[^3] ^= 1;

        Assert.Equal("signature mismatch", _filter.Check(header, tampered));
    }

    [Fact]
    public void TimestampChangedAfterSigning_IsAMismatch()
    {
        var signed = _time.GetUtcNow().ToUnixTimeSeconds().ToString();
        var header = WebhookSigner.Header((long.Parse(signed) + 1).ToString(), WebhookSigner.Hmac(WebhookSigner.Key, signed, Body));

        Assert.Equal("signature mismatch", _filter.Check(header, Body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    public void MissingOrUnreadableHeader_IsMalformed(string? header)
    {
        Assert.Equal("malformed TT-Signature", _filter.Check(header, Body));
    }

    [Theory]
    [InlineData("v2=00ff")]
    [InlineData("t=-5,v2=00ff")]
    [InlineData("t=1792324800")]
    public void HeaderWithoutValidTOrV2_IsMalformed(string decodedHeader)
    {
        Assert.Equal("malformed TT-Signature", _filter.Check(Convert.ToBase64String(Encoding.ASCII.GetBytes(decodedHeader)), Body));
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(300)]
    public void TimestampAtTolerance_IsAccepted(int offsetSeconds)
    {
        Assert.Null(_filter.Check(WebhookSigner.Sign(Body, _time.GetUtcNow().AddSeconds(offsetSeconds)), Body));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void TimestampBeyondTolerance_IsOutsideTolerance(int offsetSeconds)
    {
        Assert.Equal("timestamp outside tolerance", _filter.Check(WebhookSigner.Sign(Body, _time.GetUtcNow().AddSeconds(offsetSeconds)), Body));
    }

    [Fact]
    public void ValidWebhook_ExpiresWhenTheClockMovesPastTolerance()
    {
        var header = WebhookSigner.Sign(Body, _time.GetUtcNow());
        _time.Advance(Tolerance + TimeSpan.FromSeconds(1));

        Assert.Equal("timestamp outside tolerance", _filter.Check(header, Body));
    }

    private static DefaultHttpContext Request(string? signatureHeader)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Body);
        if (signatureHeader is not null)
        {
            context.Request.Headers["TT-Signature"] = signatureHeader;
        }

        return context;
    }
}
