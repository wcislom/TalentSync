namespace TalentSync.Matching.Domain;

/// <summary>
/// The LLM's suggestion of skills and seniority for a profile. A suggestion, never a match decision (D5).
/// </summary>
/// <remarks>
/// A value object, not an entity: it has no identity of its own and is replaced as a whole when the CV changes.
/// Model output goes through <see cref="TryCreate"/>, which reports a broken rule as a value. The constructor enforces
/// the same rules with exceptions, as a guard against our own bugs: an exception here would not end in
/// <see cref="EnrichmentStatus.NotEnriched"/> but be retried and parked by the consumer (invariant 16).
/// Exception messages name the rule only, never the model output (invariant 14).
/// </remarks>
public sealed record Enrichment
{
    public Enrichment(IEnumerable<Skill> skills, Seniority seniority, Confidence confidence)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(confidence);
        if (!Enum.IsDefined(seniority))
        {
            throw new ArgumentOutOfRangeException(nameof(seniority), "Unknown seniority.");
        }

        var distinct = skills.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            throw new ArgumentException("No skills.", nameof(skills));
        }

        Skills = Array.AsReadOnly(distinct);
        Seniority = seniority;
        Confidence = confidence;
    }

    /// <summary>Without duplicates (see <see cref="Skill"/> equality), at least one.</summary>
    public IReadOnlyList<Skill> Skills { get; }

    public Seniority Seniority { get; }

    public Confidence Confidence { get; }

    /// <summary>Validates raw model output against the domain rules. Any broken rule invalidates the whole suggestion.</summary>
    public static EnrichmentValidation TryCreate(IEnumerable<string?>? skillNames, string? seniority, double confidence)
    {
        var skills = new List<Skill>();
        foreach (var name in skillNames ?? [])
        {
            if (Skill.IsBlank(name))
            {
                return new EnrichmentValidation.Invalid(EnrichmentRejection.BlankSkill);
            }

            if (Skill.IsTooLong(name))
            {
                return new EnrichmentValidation.Invalid(EnrichmentRejection.SkillTooLong);
            }

            skills.Add(new Skill(name));
        }

        if (skills.Count == 0)
        {
            return new EnrichmentValidation.Invalid(EnrichmentRejection.NoSkills);
        }

        if (!TryParseSeniority(seniority, out var parsedSeniority))
        {
            return new EnrichmentValidation.Invalid(EnrichmentRejection.UnknownSeniority);
        }

        if (!Confidence.IsInRange(confidence))
        {
            return new EnrichmentValidation.Invalid(EnrichmentRejection.ConfidenceOutOfRange);
        }

        return new EnrichmentValidation.Valid(new Enrichment(skills, parsedSeniority, new Confidence(confidence)));
    }

    // Exact names only: Enum.TryParse alone would also accept "1", "42" or "senior".
    private static bool TryParseSeniority(string? value, out Seniority seniority) =>
        Enum.TryParse(value, ignoreCase: false, out seniority) && Enum.GetName(seniority) == value;

    // The generated equality would compare the Skills list by reference.
    public bool Equals(Enrichment? other) =>
        other is not null && Seniority == other.Seniority && Confidence == other.Confidence && Skills.SequenceEqual(other.Skills);

    public override int GetHashCode() => HashCode.Combine(Seniority, Confidence, Skills.Count);
}
