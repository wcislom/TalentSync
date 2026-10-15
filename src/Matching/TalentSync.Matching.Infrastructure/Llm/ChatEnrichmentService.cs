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
    // The limit comes from the domain, so the prompt cannot drift from the validation that rejects the answer.
    private static readonly string Instructions = $"""
        You read the CV of an IT consultant and suggest their technical skills and seniority.
        Each skill is a short name of at most {Skill.MaxLength} characters.
        Seniority is one of: Junior, Mid, Senior, Lead.
        Confidence is how sure you are of the whole assessment, from 0 to 1.
        Use only what the CV says. Answer with JSON only.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ChatOptions ChatOptions = new()
    {
        // No Temperature or other sampling options: some providers (current Claude models) answer 400 to a non-default
        // value, and every call would end in NotEnriched. The variance between calls is handled by stored results
        // (invariant 5).
        ResponseFormat = ChatResponseFormat.ForJsonSchema<EnrichmentResponseDto>(
            JsonOptions, "consultant_enrichment", "Suggested skills and seniority of a consultant."),
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

    public async Task<EnrichmentOutcome> EnrichAsync(string cvText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cvText);

        // Only the masked CV text, never the candidate id or other fields (invariant 15).
        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instructions),
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

        return Parse(response);
    }

    private EnrichmentOutcome Parse(ChatResponse response)
    {
        EnrichmentResponseDto? answer;
        try
        {
            answer = JsonSerializer.Deserialize<EnrichmentResponseDto>(response.Text, JsonOptions);
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
