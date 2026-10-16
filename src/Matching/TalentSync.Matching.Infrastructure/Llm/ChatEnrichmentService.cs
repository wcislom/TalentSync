using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Infrastructure.Llm;

/// <summary>
/// <see cref="IEnrichmentService"/> over any <see cref="IChatClient"/>: masks the CV, asks for JSON of a fixed schema,
/// validates it with the domain rules. Every failure is an outcome, never an exception (D5).
/// </summary>
/// <remarks>
/// A singleton, so the circuit breaker sees every call of the worker: during an outage later events get
/// <see cref="EnrichmentFailure.Unavailable"/> at once instead of each waiting for the timeout.
/// </remarks>
internal sealed class ChatEnrichmentService : IEnrichmentService
{
    private const string OutcomeTag = "talentsync.enrichment.outcome";

    // A reused stored suggestion makes no call, so it has no span here: in a trace, a profile upsert without an
    // enrichment span before it means the stored suggestion was used.
    private static readonly ActivitySource ActivitySource = new("TalentSync.Matching");
    private static readonly Meter Meter = new("TalentSync.Matching");

    private static readonly Counter<long> Calls = Meter.CreateCounter<long>(
        "talentsync.enrichment.calls", description: "Model calls by outcome.");

    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>(
        "talentsync.enrichment.tokens", unit: "{token}", description: "Tokens billed by the provider.");

    private static readonly ChatOptions ChatOptions = new()
    {
        // No Temperature or other sampling options: some providers (current Claude models) answer 400 to a non-default
        // value, and every call would end in NotEnriched. The variance between calls is handled by stored results
        // (invariant 5).
        ResponseFormat = EnrichmentPrompt.ResponseFormat,
    };

    private readonly IChatClient _chatClient;
    private readonly ILogger<ChatEnrichmentService> _logger;
    private readonly ResiliencePipeline _pipeline;

    public ChatEnrichmentService(
        IChatClient chatClient, IOptions<LlmOptions> options, TimeProvider timeProvider, ILogger<ChatEnrichmentService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;

        var breaker = options.Value.CircuitBreaker;

        // No retry (ADR 007): cost and rate limits, and a failure ends in NotEnriched anyway. The breaker is outside
        // the timeout so that timeouts count as failures. Its default predicate skips OperationCanceledException,
        // so a shutdown does not open the circuit. Invalid output is checked outside the pipeline: a model that
        // answers badly is not down.
        _pipeline = new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = breaker.FailureRatio,
                MinimumThroughput = breaker.MinimumThroughput,
                SamplingDuration = breaker.SamplingDuration,
                BreakDuration = breaker.BreakDuration,
            })
            .AddTimeout(options.Value.Timeout)
            .Build();
    }

    public string PromptVersion => EnrichmentPrompt.Version;

    /// <remarks>
    /// The span and the metrics carry the outcome, the model and token counts only: never the prompt, the CV or the
    /// model's answer (invariant 14).
    /// </remarks>
    public async Task<EnrichmentOutcome> EnrichAsync(string cvText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cvText);

        using var activity = ActivitySource.StartActivity("enrichment", ActivityKind.Client);
        activity?.SetTag("talentsync.prompt_version", PromptVersion);

        var outcome = await CallModelAsync(cvText, activity, cancellationToken);

        var outcomeName = outcome switch
        {
            EnrichmentOutcome.Enriched => "enriched",
            EnrichmentOutcome.Failed { Reason: EnrichmentFailure.Timeout } => "timeout",
            EnrichmentOutcome.Failed { Reason: EnrichmentFailure.Unavailable } => "unavailable",
            EnrichmentOutcome.Failed { Reason: EnrichmentFailure.InvalidOutput } => "invalid_output",
            _ => throw new InvalidOperationException("Unknown enrichment outcome."),
        };
        activity?.SetTag(OutcomeTag, outcomeName);
        if (outcome is EnrichmentOutcome.Failed)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        Calls.Add(1, new KeyValuePair<string, object?>(OutcomeTag, outcomeName));
        return outcome;
    }

    private async Task<EnrichmentOutcome> CallModelAsync(string cvText, Activity? activity, CancellationToken cancellationToken)
    {
        // Only the masked CV text, never the candidate id or other fields (invariant 15).
        List<ChatMessage> messages =
        [
            new(ChatRole.System, EnrichmentPrompt.Instructions),
            new(ChatRole.User, CvMasker.Mask(cvText)),
        ];

        ChatResponse response;
        try
        {
            response = await _pipeline.ExecuteAsync(
                async token => await _chatClient.GetResponseAsync(messages, ChatOptions, token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown: the event is processed again by the next owner of the partition. Turning this into
            // NotEnriched would upsert a profile without a suggestion only because the worker was stopping.
            throw;
        }
        catch (TimeoutRejectedException)
        {
            return Fail(EnrichmentFailure.Timeout);
        }
        catch (Exception exception)
        {
            // Open circuit, 429, 5xx, a provider SDK's own exception type: whatever the provider throws, the profile
            // must not wait for the model (D5). The exception type only; its message may echo the request.
            _logger.LogWarning("LLM call failed: {ExceptionType}", exception.GetType().Name);
            return Fail(EnrichmentFailure.Unavailable);
        }

        RecordUsage(response, activity);
        return Parse(response);
    }

    private static void RecordUsage(ChatResponse response, Activity? activity)
    {
        activity?.SetTag("gen_ai.response.model", response.ModelId);
        activity?.SetTag("gen_ai.usage.input_tokens", response.Usage?.InputTokenCount);
        activity?.SetTag("gen_ai.usage.output_tokens", response.Usage?.OutputTokenCount);

        if (response.Usage?.InputTokenCount is { } input)
        {
            Tokens.Add(input, new KeyValuePair<string, object?>("gen_ai.token.type", "input"));
        }

        if (response.Usage?.OutputTokenCount is { } output)
        {
            Tokens.Add(output, new KeyValuePair<string, object?>("gen_ai.token.type", "output"));
        }
    }

    private EnrichmentOutcome Parse(ChatResponse response)
    {
        EnrichmentResponseDto? answer;
        try
        {
            answer = JsonSerializer.Deserialize<EnrichmentResponseDto>(response.Text, EnrichmentPrompt.JsonOptions);
        }
        catch (JsonException)
        {
            return Fail(EnrichmentFailure.InvalidOutput);
        }

        if (answer?.Confidence is not { } confidence)
        {
            return Fail(EnrichmentFailure.InvalidOutput);
        }

        switch (Enrichment.TryCreate(answer.Skills, answer.Seniority, confidence))
        {
            case EnrichmentValidation.Valid valid:
                return new EnrichmentOutcome.Enriched(valid.Enrichment, response.ModelId ?? "unknown");

            case EnrichmentValidation.Invalid invalid:
                _logger.LogWarning("LLM output rejected: {Rejection}", invalid.Reason);
                return Fail(EnrichmentFailure.InvalidOutput);

            default:
                throw new InvalidOperationException("Unknown enrichment validation result.");
        }
    }

    private EnrichmentOutcome.Failed Fail(EnrichmentFailure reason)
    {
        _logger.LogWarning("Enrichment failed: {EnrichmentFailure}", reason);
        return new EnrichmentOutcome.Failed(reason);
    }
}
