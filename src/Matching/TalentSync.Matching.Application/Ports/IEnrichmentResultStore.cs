using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Application.Ports;

/// <summary>
/// Suggestions already received from the model, keyed by candidate, CV and prompt version. The model is non-deterministic, so a
/// suggestion is stored before the profile is upserted and a retry replays it instead of asking again (invariant 5).
/// </summary>
public interface IEnrichmentResultStore
{
    /// <summary>The suggestion stored for this CV of the candidate and this prompt, or null when there is none.</summary>
    Task<Enrichment?> FindAsync(CandidateId candidateId, CvHash cvHash, string promptVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the suggestion and returns the one that is stored now. When another processor stored one for the same
    /// candidate, CV and prompt first, that one is returned, so the profile always matches what is stored.
    /// </summary>
    Task<Enrichment> SaveAsync(
        CandidateId candidateId, CvHash cvHash, string promptVersion, Enrichment enrichment, string modelId,
        CancellationToken cancellationToken);
}
