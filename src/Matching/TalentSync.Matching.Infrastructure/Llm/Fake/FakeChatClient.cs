using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace TalentSync.Matching.Infrastructure.Llm.Fake;

/// <summary>
/// Stands in for a real model in tests and demos (<c>Llm:Provider = Fake</c>). A marker such as <c>[llm:Hang]</c> in
/// the CV text picks the mode for that call; without one, <c>Llm:Fake:Mode</c> applies.
/// </summary>
/// <remarks>
/// The mode is per call, not a switch on the host: all scenarios share one host, so a switch could let an event of an
/// earlier scenario run in the next scenario's mode (ADR 007). A real model never sees the marker's meaning.
/// </remarks>
internal sealed partial class FakeChatClient(IOptions<LlmOptions> options) : IChatClient
{
    public const string ModelId = "fake";

    private const string ValidAnswer = """{"skills":["C#",".NET","SQL Server"],"seniority":"Senior","confidence":0.8}""";

    private readonly ConcurrentQueue<string> _received = new();

    /// <summary>The user message of every call, i.e. the CV text as the model received it (masked).</summary>
    public IReadOnlyCollection<string> ReceivedCvTexts => _received;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken cancellationToken = default)
    {
        var cvText = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        _received.Enqueue(cvText);

        var answer = ModeOf(cvText) switch
        {
            FakeLlmMode.Valid => ValidAnswer,
            FakeLlmMode.InvalidJson => """{"skills":["C#",""",
            FakeLlmMode.OutOfRange => """{"skills":["C#"],"seniority":"Senior","confidence":1.5}""",
            FakeLlmMode.Hang => await HangAsync(cancellationToken),
            _ => throw new InvalidOperationException("Unknown fake LLM mode."),
        };

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { ModelId = ModelId };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Enrichment does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private FakeLlmMode ModeOf(string cvText) =>
        ModeMarker().Match(cvText) is { Success: true } marker && Enum.TryParse<FakeLlmMode>(marker.Groups[1].Value, out var mode)
            ? mode
            : options.Value.Fake.Mode;

    // Ends only through the caller's timeout or shutdown, like a model that never answers.
    private static async Task<string> HangAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }

    [GeneratedRegex(@"\[llm:(\w+)\]")]
    private static partial Regex ModeMarker();
}
