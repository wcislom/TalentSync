namespace TalentSync.Matching.Domain;

/// <summary>
/// An LLM suggestion of skills and seniority that passed domain validation. A suggestion, never a decision (D5).
/// </summary>
public sealed class EnrichmentSuggestion
{
    public const int MaxSkills = 30;

    private EnrichmentSuggestion(IReadOnlyList<Skill> skills, Seniority seniority, Confidence confidence)
    {
        Skills = skills;
        Seniority = seniority;
        Confidence = confidence;
    }

    /// <summary>Trimmed, without case-insensitive duplicates, at most <see cref="MaxSkills"/>.</summary>
    public IReadOnlyList<Skill> Skills { get; }

    public Seniority Seniority { get; }

    public Confidence Confidence { get; }

    /// <summary>
    /// Validates raw model output. The input is untrusted (invariant 16): any rule broken makes the whole suggestion
    /// invalid rather than partially accepted, so the profile falls back to <see cref="EnrichmentStatus.NotEnriched"/>.
    /// </summary>
    public static EnrichmentSuggestionValidation Validate(IReadOnlyList<string?>? skillNames, string? seniority, double confidence)
    {
        // Enum.TryParse would also accept "2", "senior" or "Senior, Lead".
        if (seniority is null || !Enum.GetNames<Seniority>().Contains(seniority, StringComparer.Ordinal))
        {
            return EnrichmentSuggestionValidation.Invalid("seniority is not one of Junior, Mid, Senior, Lead");
        }

        if (!Confidence.TryCreate(confidence, out var validConfidence))
        {
            return EnrichmentSuggestionValidation.Invalid("confidence is outside [0, 1]");
        }

        if (skillNames is null || skillNames.Any(string.IsNullOrWhiteSpace))
        {
            return EnrichmentSuggestionValidation.Invalid("a skill name is empty");
        }

        var skills = Array.AsReadOnly(skillNames.Select(name => new Skill(name!)).Distinct().ToArray());
        if (skills.Count > MaxSkills)
        {
            return EnrichmentSuggestionValidation.Invalid($"more than {MaxSkills} skills");
        }

        return EnrichmentSuggestionValidation.Valid(new EnrichmentSuggestion(skills, Enum.Parse<Seniority>(seniority), validConfidence));
    }
}
