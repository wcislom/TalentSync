namespace TalentSync.Matching.Infrastructure.MatchingPlatform;

// The body of PUT /profiles/{candidateId}. Enums travel as their names; the Matching Platform rejects an unknown one (400).
internal sealed record ProfileRequestDto(IReadOnlyList<string> Skills, string? Seniority, double? Confidence, string EnrichmentStatus);
