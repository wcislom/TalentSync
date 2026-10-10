namespace TalentSync.Recruitment.Domain;

/// <summary>
/// Result of reading a resource from the recruitment source. Permanent outcomes are values; transient failures
/// (timeout, 5xx, 429, 401, 403) are exceptions and never appear here.
/// </summary>
public abstract record FetchResult<T>
    where T : notnull
{
    private FetchResult()
    {
    }

    public sealed record Found(T Value) : FetchResult<T>;

    /// <summary>The resource no longer exists. An expected outcome, not an error.</summary>
    public sealed record Deleted : FetchResult<T>;

    /// <summary>
    /// The resource cannot be read or mapped: a permanent failure, parked by the worker.
    /// <paramref name="Reason"/> holds a status code and an endpoint or field name only, never a response body
    /// or PII (invariant 14).
    /// </summary>
    public sealed record Rejected(string Reason) : FetchResult<T>;
}
