namespace TalentSync.Recruitment.Domain;

/// <summary>
/// Our interpretation of the customer-defined stage of a job application. The adapter maps the stage name
/// to a status using configuration, never a hard-coded name (D2).
/// </summary>
public enum ApplicationStatus
{
    NotQualified,
    Qualified,
}
