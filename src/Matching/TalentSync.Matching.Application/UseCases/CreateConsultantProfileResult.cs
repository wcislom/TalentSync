namespace TalentSync.Matching.Application.UseCases;

/// <summary>
/// What <see cref="CreateConsultantProfileUseCase"/> made of one job application. Transient failures are not
/// results: they are exceptions, classified by the consumer skeleton.
/// </summary>
public abstract record CreateConsultantProfileResult
{
    private CreateConsultantProfileResult()
    {
    }

    public static CreateConsultantProfileResult Upserted { get; } = new UpsertedResult();

    public static CreateConsultantProfileResult Skipped { get; } = new SkippedResult();

    public static CreateConsultantProfileResult Rejected(string reason) => new RejectedResult(reason);

    /// <summary>The application is qualified and the profile is in the Matching Platform.</summary>
    public sealed record UpsertedResult : CreateConsultantProfileResult;

    /// <summary>
    /// The application is not qualified, or the application or its candidate no longer exists: no profile is created
    /// or changed (D2, D4).
    /// </summary>
    public sealed record SkippedResult : CreateConsultantProfileResult;

    /// <summary>
    /// The recruitment source or the Matching Platform refused for good. <paramref name="Reason"/> holds a status code
    /// and an endpoint or field name only, never a response body or PII (invariant 14).
    /// </summary>
    public sealed record RejectedResult(string Reason) : CreateConsultantProfileResult;
}
