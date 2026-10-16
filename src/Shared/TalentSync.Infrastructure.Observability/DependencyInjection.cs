using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TalentSync.Infrastructure.Observability;

/// <summary>Exports traces, metrics and logs of one host over OTLP (ADR 010).</summary>
public static class DependencyInjection
{
    /// <summary>Every <c>ActivitySource</c> and <c>Meter</c> of ours is named with this prefix.</summary>
    public const string SourcePrefix = "TalentSync.";

    private const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string ServiceNameKey = "OTEL_SERVICE_NAME";

    /// <summary>
    /// Registers no exporter unless <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set: without one every span would be
    /// created and dropped, and tests and CI would try to reach a collector that is not there.
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        // Registered with or without an exporter: whose context a request continues is a trust decision, not an export
        // setting, and tests must see the same traces as a running system. ASP.NET Core resolves it from DI.
        services.AddSingleton<DistributedContextPropagator>(new EdgeTraceContextPropagator());

        var endpoint = configuration[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return services;
        }

        // The SDK would fall back to "unknown_service", and three hosts would merge into one in the dashboard.
        var serviceName = configuration[ServiceNameKey]
            ?? throw new InvalidOperationException($"Missing configuration '{ServiceNameKey}'.");
        var collector = new Uri(endpoint);

        // With the SDK's default (W3C trace context and baggage) the ASP.NET Core instrumentation extracts the incoming
        // context a second time and replaces the request span with one parented to the sender's trace. With exactly
        // TraceContextPropagator it trusts the request span, which EdgeTraceContextPropagator has made a root. That is
        // the instrumentation's internal behaviour, pinned by EdgeTraceTests. Process-wide, like the default it replaces.
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                // Every request is a root (EdgeTraceContextPropagator), so ParentBased would sample everything too;
                // AlwaysOn says so without depending on that.
                .SetSampler(new AlwaysOnSampler())
                .AddSource(SourcePrefix + "*")
                // Liveness probes would bury the flow.
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation(options => options.EnrichWithHttpRequestMessage = HttpPeerName.Enrich)
                // EF Core sends its commands through SqlClient. Parameter values are never recorded
                // (SetDbQueryParameters is off) and EF does not inline them, so db.query.text holds no PII (invariant 14).
                .AddSqlClientInstrumentation()
                // Processors run in the order they are added: the policy must see a span before the exporter does.
                .AddProcessor(new SpanExportPolicy())
                .AddOtlpExporter(options => UseCollector(options, collector)))
            .WithMetrics(metrics => metrics
                .AddMeter(SourcePrefix + "*")
                .AddAspNetCoreInstrumentation()
                // Includes the Blob SDK's polling of checkpoints and ownership, told apart by server.address: unlike
                // the spans, these metrics cannot be filtered per request.
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddMeter("System.Runtime")
                .AddOtlpExporter(options => UseCollector(options, collector)))
            .WithLogging(
                logging => logging.AddOtlpExporter(options => UseCollector(options, collector)),
                options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, HttpPeerName.Filter>());

        return services;
    }

    private static void UseCollector(OtlpExporterOptions options, Uri collector)
    {
        options.Endpoint = collector;
        options.Protocol = OtlpExportProtocol.Grpc;
    }
}
