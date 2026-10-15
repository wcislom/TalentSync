using Polly.CircuitBreaker;
using TalentSync.Matching.Infrastructure.Llm.Fake;

namespace TalentSync.Matching.Infrastructure.Llm;

internal sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public const string FakeProvider = "Fake";

    /// <summary>Only <see cref="FakeProvider"/> exists until a real provider is wired.</summary>
    public string? Provider { get; set; }

    /// <summary>
    /// How long one event may wait for the model. Every later event of the partition waits too (ADR 007), so this is
    /// the upper bound of the delay a slow model adds per event.
    /// </summary>
    public TimeSpan Timeout { get; set; }

    /// <summary>Only the numbers are bound; which exceptions count is decided in code.</summary>
    public CircuitBreakerStrategyOptions CircuitBreaker { get; set; } = new();

    public FakeLlmOptions Fake { get; set; } = new();

    public bool IsValid() => Provider == FakeProvider && Timeout > TimeSpan.Zero;
}
