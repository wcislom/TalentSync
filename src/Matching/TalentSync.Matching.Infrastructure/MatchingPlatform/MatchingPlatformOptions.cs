namespace TalentSync.Matching.Infrastructure.MatchingPlatform;

/// <summary>What the Matching Platform client reads from the <c>MatchingPlatform</c> configuration section.</summary>
internal sealed class MatchingPlatformOptions
{
    public const string SectionName = "MatchingPlatform";

    public Uri? BaseUrl { get; set; }

    public bool IsValid() => BaseUrl is { IsAbsoluteUri: true };
}
