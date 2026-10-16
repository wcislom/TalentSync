using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace TalentSync.Infrastructure.Observability.Tests;

/// <summary>
/// <c>AddObservability</c> with the SDK enabled, as in a running host. Scenario O1 runs without an endpoint, so only
/// this test covers the ASP.NET Core instrumentation, which extracts the incoming context a second time and would
/// replace the request span before the endpoint runs.
/// </summary>
public sealed class EdgeTraceTests
{
    [Fact]
    public async Task RequestWithSenderTraceContext_StartsANewTrace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Nothing listens there: the exporter fails quietly, and the test reads the span in process.
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:9",
            ["OTEL_SERVICE_NAME"] = "observability-tests",
        });
        builder.Services.AddObservability(builder.Configuration);

        await using var app = builder.Build();
        Activity? requestSpan = null;
        app.MapGet("/webhook", () =>
        {
            requestSpan = Activity.Current;
            return Results.Ok();
        });
        await app.StartAsync(cancellationToken);

        var senderTrace = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/webhook");
        request.Headers.Add("traceparent", $"00-{senderTrace}-{ActivitySpanId.CreateRandom()}-01");
        using var response = await app.GetTestClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        Assert.NotNull(requestSpan);
        Assert.Equal(ActivityKind.Server, requestSpan.Kind);
        Assert.True(requestSpan.Recorded);
        Assert.Equal(default, requestSpan.ParentSpanId);
        Assert.NotEqual(senderTrace, requestSpan.TraceId);
    }
}
