using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Application.Ports;

/// <summary>
/// Port to the Matching Platform. The upsert is keyed by the candidate id, so repeating it creates no second
/// profile (D3, invariant 2).
/// </summary>
public interface IMatchingPlatform
{
    /// <summary>Creates or replaces the consultant's profile.</summary>
    Task<MatchingPlatformResult> UpsertProfileAsync(ConsultantProfile profile, CancellationToken cancellationToken);
}
