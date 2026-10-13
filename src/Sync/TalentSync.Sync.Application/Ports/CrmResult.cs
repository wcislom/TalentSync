namespace TalentSync.Sync.Application.Ports;

/// <summary>
/// What the CRM made of one effect. Transient failures are not results: they are exceptions, classified by the
/// consumer skeleton.
/// </summary>
public abstract record CrmResult
{
    private CrmResult()
    {
    }

    public static CrmResult Done { get; } = new DoneResult();

    public static CrmResult Rejected(string reason) => new RejectedResult(reason);

    public sealed record DoneResult : CrmResult;

    /// <summary>
    /// The CRM refused the request for good (an unexpected 4xx). <paramref name="Reason"/> holds a status code and an
    /// endpoint only, never a response body or PII (invariant 14).
    /// </summary>
    public sealed record RejectedResult(string Reason) : CrmResult;
}
