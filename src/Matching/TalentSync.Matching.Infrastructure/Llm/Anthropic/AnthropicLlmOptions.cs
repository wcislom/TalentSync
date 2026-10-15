namespace TalentSync.Matching.Infrastructure.Llm.Anthropic;

internal sealed class AnthropicLlmOptions
{
    /// <summary>User-secrets or environment only, never in the repo (invariant 17).</summary>
    public string? ApiKey { get; set; }

    public string? Model { get; set; }

    /// <summary>
    /// Includes the tokens of adaptive thinking, which current models do by default. Too low a ceiling cuts the JSON
    /// off, which ends in <c>InvalidOutput</c>, not in an error.
    /// </summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>
    /// The SDK's own retries of 408/409/429/5xx and connection failures, with its back-off and <c>retry-after</c>.
    /// They run inside <see cref="LlmOptions.Timeout"/>, which bounds the whole call (ADR 007).
    /// </summary>
    public int MaxRetries { get; set; }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model) && MaxOutputTokens > 0 && MaxRetries >= 0;
}
