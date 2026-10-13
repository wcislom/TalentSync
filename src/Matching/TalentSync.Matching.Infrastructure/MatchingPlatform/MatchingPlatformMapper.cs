using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Infrastructure.MatchingPlatform;

/// <summary>Translates a consultant profile into the Matching Platform's profile.</summary>
internal static class MatchingPlatformMapper
{
    public static ProfileRequestDto ToProfileRequest(ConsultantProfile profile) =>
        new(
            profile.Skills.Select(skill => skill.Name).ToList(),
            profile.Seniority?.ToString(),
            profile.Confidence,
            profile.EnrichmentStatus.ToString());
}
