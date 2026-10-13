namespace TalentSync.Matching.Domain;

/// <summary>Whether the profile carries an LLM suggestion. <see cref="NotEnriched"/> is a valid profile, not an error (D5).</summary>
public enum EnrichmentStatus
{
    Enriched,
    NotEnriched,
}
