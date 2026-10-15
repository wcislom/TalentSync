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

    public int MaxRetries { get; set; }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model) && MaxOutputTokens > 0 && MaxRetries >= 0;
}
