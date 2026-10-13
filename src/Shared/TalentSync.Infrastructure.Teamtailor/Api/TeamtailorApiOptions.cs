namespace TalentSync.Infrastructure.Teamtailor.Api;

/// <summary>What the API client reads from the <c>Teamtailor</c> configuration section.</summary>
internal sealed class TeamtailorApiOptions
{
    public const string SectionName = "Teamtailor";

    public Uri? BaseUrl { get; set; }

    public string ApiKey { get; set; } = "";

    /// <summary>Sent as <c>X-Api-Version</c>; Teamtailor rejects requests without it.</summary>
    public string ApiVersion { get; set; } = "";

    /// <summary>
    /// The customer-defined stage that means qualified (D2). A recruiter renaming the stage without this being
    /// updated makes every application "not qualified", silently (known limitation).
    /// </summary>
    public string QualifiedStageName { get; set; } = "";

    public bool IsValid() =>
        BaseUrl is { IsAbsoluteUri: true }
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiVersion)
        && !string.IsNullOrWhiteSpace(QualifiedStageName);
}
