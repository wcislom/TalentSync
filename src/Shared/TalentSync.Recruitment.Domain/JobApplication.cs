namespace TalentSync.Recruitment.Domain;

/// <summary>
/// Read-only snapshot of one candidate's application to one job. A candidate has many.
/// </summary>
public sealed record JobApplication(string Id, string CandidateId, ApplicationStatus Status);
