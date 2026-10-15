using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
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
    private const string ServiceNameKey = "Observability:ServiceName";

    /// <summary>
    /// Registers nothing unless <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set: without an exporter every span would be
    /// created and dropped, and tests and CI would try to reach a collector that is not there.
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return services;
        }

        var serviceName = configuration[ServiceNameKey]
            ?? throw new InvalidOperationException($"Missing configuration '{ServiceNameKey}'.");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                // The default ParentBased sampler obeys the sampled flag of an incoming traceparent, so a webhook
                // sender with an unsampled trace (the simulator does this) would switch off our whole flow.
                .SetSampler(new AlwaysOnSampler())
                .AddSource(SourcePrefix + "*")
                // Liveness probes and the Blob SDK's checkpoint and ownership polling would bury the flow.
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation(options => options.FilterHttpRequestMessage = request => !IsAzureSdk(request)))
            .WithMetrics(metrics => metrics
                .AddMeter(SourcePrefix + "*")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("System.Runtime"))
            .WithLogging(_ => { }, options =>
            {
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
            })
            .UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri(endpoint));

        return services;
    }

    private static bool IsAzureSdk(HttpRequestMessage request) =>
        request.Headers.UserAgent.ToString().StartsWith("azsdk-net-", StringComparison.Ordinal);
}
