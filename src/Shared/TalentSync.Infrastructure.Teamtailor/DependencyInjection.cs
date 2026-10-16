using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using TalentSync.Infrastructure.Teamtailor.Api;
using TalentSync.Infrastructure.Teamtailor.Webhooks;
using TalentSync.Recruitment.Domain;

namespace TalentSync.Infrastructure.Teamtailor;

/// <summary>
/// The only public entry points of the adapter besides the webhook interfaces (dependency rules in
/// <c>docs/architecture.md</c>). Two of them, because Ingress needs only the webhooks and the workers only the API,
/// and each validates its own configuration on start. Implementations stay internal.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Webhook parsing for Ingress.</summary>
    /// <param name="configuration">Not read yet. The signature verifier will bind the <c>Teamtailor</c> section from it.</param>
    public static IServiceCollection AddTeamtailorWebhooks(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Stateless, so one instance serves every request.
        services.AddSingleton<IWebhookParser, TeamtailorWebhookParser>();
        return services;
    }

    /// <summary>
    /// <see cref="IRecruitmentSource"/> over the Teamtailor REST API, for the workers. Reads <c>Teamtailor:BaseUrl</c>,
    /// <c>ApiKey</c>, <c>ApiVersion</c>, <c>QualifiedStageName</c>, and the standard resilience handler's options from
    /// <c>Teamtailor:Resilience</c>.
    /// </summary>
    public static IServiceCollection AddTeamtailorApi(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TeamtailorApiOptions.SectionName);
        services.AddOptions<TeamtailorApiOptions>()
            .Bind(section)
            .Validate(o => o.IsValid(), $"'{TeamtailorApiOptions.SectionName}' needs an absolute BaseUrl, ApiKey, ApiVersion and QualifiedStageName.")
            .ValidateOnStart();

        // HTTP pipeline, outermost first: one rate-limit retry, then the standard handler (total timeout, retry,
        // circuit breaker, attempt timeout) for short blips. Long outages are waited out by the handler loop, which
        // runs this whole pipeline again on every attempt.
        // The client name labels its HTTP spans and resilience logs.
        var apiClient = services.AddHttpClient<TeamtailorApiClient>("teamtailor", (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TeamtailorApiOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", $"token={options.ApiKey}");
            client.DefaultRequestHeaders.Add("X-Api-Version", options.ApiVersion);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        });

        // 429 is retried once, after the reset, and only here. A second 429 reaches the client, which throws it as
        // transient (scenario S5 expects exactly 2 GETs). Without the header there is nothing to wait for: the 429
        // goes straight to the handler loop's back-off.
        // Plain Polly options, not HttpRetryStrategyOptions: its ShouldRetryAfterHeader setter replaces DelayGenerator.
        apiClient.AddResilienceHandler("teamtailor-rate-limit", pipeline => pipeline.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = args => ValueTask.FromResult(TeamtailorRateLimit.TryGetReset(args.Outcome.Result, out _)),
            DelayGenerator = args => ValueTask.FromResult(
                TeamtailorRateLimit.TryGetReset(args.Outcome.Result, out var reset) ? reset : (TimeSpan?)null),
            MaxRetryAttempts = 1,
        }));

        apiClient.AddStandardResilienceHandler(section.GetSection("Resilience"))
            .Configure(options =>
            {
                options.Retry.ShouldHandle = args => ValueTask.FromResult(TeamtailorRateLimit.IsTransientExceptRateLimit(args.Outcome));
                options.CircuitBreaker.ShouldHandle = args => ValueTask.FromResult(TeamtailorRateLimit.IsTransientExceptRateLimit(args.Outcome));
            });

        services.AddTransient<IRecruitmentSource, TeamtailorRecruitmentSource>();

        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
