namespace TalentSync.Matching.Application.Ports;

/// <summary>
/// What the Matching Platform made of one effect. Transient failures are not results: they are exceptions,
/// classified by the consumer skeleton.
/// </summary>
public abstract record MatchingPlatformResult
{
    private MatchingPlatformResult()
    {
    }

    public static MatchingPlatformResult Done { get; } = new DoneResult();

    public static MatchingPlatformResult Rejected(string reason) => new RejectedResult(reason);

    public sealed record DoneResult : MatchingPlatformResult;

    /// <summary>
    /// The Matching Platform refused the request for good (an unexpected 4xx). <paramref name="Reason"/> holds a status
    /// code and an endpoint only, never a response body (invariant 14).
    /// </summary>
    public sealed record RejectedResult(string Reason) : MatchingPlatformResult;
}
