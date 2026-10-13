namespace TalentSync.Sync.Application.UseCases;

/// <summary>
/// What <see cref="SyncCandidateToCrmUseCase"/> made of one candidate. Transient failures are not results: they are
/// exceptions, classified by the consumer skeleton.
/// </summary>
public abstract record SyncCandidateToCrmResult
{
    private SyncCandidateToCrmResult()
    {
    }

    public static SyncCandidateToCrmResult Done { get; } = new DoneResult();

    public static SyncCandidateToCrmResult Rejected(string reason) => new RejectedResult(reason);

    /// <summary>The CRM contact matches the candidate's current state: upserted, or marked as deleted.</summary>
    public sealed record DoneResult : SyncCandidateToCrmResult;

    /// <summary>
    /// The recruitment source or the CRM refused for good. <paramref name="Reason"/> holds a status code and an
    /// endpoint or field name only, never a response body or PII (invariant 14).
    /// </summary>
    public sealed record RejectedResult(string Reason) : SyncCandidateToCrmResult;
}
