namespace TalentSync.Contracts;

/// <summary>
/// Event names carried in <see cref="EventEnvelope.Type"/>. Consumers ignore names they do not know.
/// </summary>
public static class EventTypes
{
    public const string CandidatePrefix = "candidate.";
    public const string CandidateCreate = "candidate.create";
    public const string CandidateUpdate = "candidate.update";
    public const string CandidateDestroy = "candidate.destroy";

    public const string JobApplicationPrefix = "job_application.";
    public const string JobApplicationCreate = "job_application.create";
    public const string JobApplicationUpdate = "job_application.update";
    public const string JobApplicationDestroy = "job_application.destroy";

    public static bool IsCandidateEvent(string type) =>
        type.StartsWith(CandidatePrefix, StringComparison.Ordinal);

    public static bool IsJobApplicationEvent(string type) =>
        type.StartsWith(JobApplicationPrefix, StringComparison.Ordinal);
}
