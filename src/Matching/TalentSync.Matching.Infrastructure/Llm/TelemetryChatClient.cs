using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;

namespace TalentSync.Matching.Infrastructure.Llm;

/// <summary>
/// Decorates the provider's <see cref="IChatClient"/> with a <c>chat {model}</c> span and a counter of billed tokens
/// (ADR 010; names follow the OpenTelemetry gen_ai conventions). Under the <c>enrich</c> span it separates the
/// provider's answer from our validation: <c>invalid_output</c> with a successful <c>chat</c> span means the model
/// answered and our rules rejected it.
/// </summary>
/// <remarks>Only <see cref="GetResponseAsync"/> is instrumented: enrichment never streams.</remarks>
internal sealed class TelemetryChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private static readonly Counter<long> Tokens = MatchingTelemetry.Meter.CreateCounter<long>(
        "talentsync.llm.tokens", unit: "{token}", description: "Tokens billed by the provider.");

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var model = this.GetService<ChatClientMetadata>()?.DefaultModelId;
        using var activity = MatchingTelemetry.Source.StartActivity(model is null ? "chat" : $"chat {model}", ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.request.model", model);

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception exception)
        {
            // The type only: a provider's exception message may echo the request (invariant 14).
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }

        activity?.SetTag("gen_ai.response.model", response.ModelId);
        activity?.SetTag("gen_ai.usage.input_tokens", response.Usage?.InputTokenCount);
        activity?.SetTag("gen_ai.usage.output_tokens", response.Usage?.OutputTokenCount);
        Count(response.Usage?.InputTokenCount, "input");
        Count(response.Usage?.OutputTokenCount, "output");
        return response;
    }

    private static void Count(long? tokens, string type)
    {
        if (tokens is { } count)
        {
            Tokens.Add(count, new KeyValuePair<string, object?>("gen_ai.token.type", type));
        }
    }
}
