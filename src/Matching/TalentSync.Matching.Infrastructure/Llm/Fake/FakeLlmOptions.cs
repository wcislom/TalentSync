namespace TalentSync.Matching.Infrastructure.Llm.Fake;

internal sealed class FakeLlmOptions
{
    /// <summary>Used when the CV text carries no mode marker.</summary>
    public FakeLlmMode Mode { get; set; } = FakeLlmMode.Valid;
}

internal enum FakeLlmMode
{
    Valid,
    InvalidJson,
    OutOfRange,
    Hang,
}
