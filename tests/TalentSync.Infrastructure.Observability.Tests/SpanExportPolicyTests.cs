using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace TalentSync.Infrastructure.Observability.Tests;

/// <summary>The policy in front of an exporter, registered in the same order as in <c>AddObservability</c>.</summary>
public sealed class SpanExportPolicyTests : IDisposable
{
    private readonly ActivitySource _source = new($"{nameof(SpanExportPolicyTests)}.{Guid.NewGuid()}");
    private readonly List<Activity> _exported = [];
    private readonly TracerProvider _provider;

    public SpanExportPolicyTests()
    {
        _provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(_source.Name)
            .AddProcessor(new SpanExportPolicy())
            .AddInMemoryExporter(_exported)
            .Build();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _source.Dispose();
    }

    [Fact]
    public void ClientSpanWithoutParent_IsNotExported()
    {
        using (_source.StartActivity("poll outbox", ActivityKind.Client))
        {
        }

        Assert.Empty(_exported);
    }

    [Fact]
    public void ClientSpanInsideAFlow_IsExported()
    {
        using (_source.StartActivity("process"))
        using (_source.StartActivity("insert inbox", ActivityKind.Client))
        {
        }

        Assert.Contains(_exported, span => span.DisplayName == "insert inbox");
    }

    [Fact]
    public void FailedSpan_KeepsItsStatusButNotTheMessage()
    {
        using (var activity = _source.StartActivity("insert outbox"))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Violation of UNIQUE KEY constraint. The duplicate key value is (alice@example.com).");
        }

        var span = Assert.Single(_exported);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Null(span.StatusDescription);
    }
}
