namespace TalentSync.Matching.Domain;

/// <summary>
/// The LLM's suggestion of skills and seniority for a profile. A suggestion, never a match decision (D5).
/// </summary>
/// <remarks>
/// A value object, not an entity: it has no identity of its own and is replaced as a whole when the CV changes.
/// Its constructor enforces the domain rules, so raw model output that breaks one never becomes an enrichment
/// (invariant 16). Exception messages name the rule only, never the model output (invariant 14).
/// </remarks>
public sealed record Enrichment
{
    public const int MaxSkills = 30;

    public Enrichment(IEnumerable<Skill> skills, Seniority seniority, Confidence confidence)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(confidence);
        if (!Enum.IsDefined(seniority))
        {
            throw new ArgumentOutOfRangeException(nameof(seniority), "Unknown seniority.");
        }

        var distinct = skills.Distinct().ToArray();
        if (distinct.Length > MaxSkills)
        {
            throw new ArgumentException($"More than {MaxSkills} skills.", nameof(skills));
        }

        Skills = Array.AsReadOnly(distinct);
        Seniority = seniority;
        Confidence = confidence;
    }

    /// <summary>Without duplicates (see <see cref="Skill"/> equality), at most <see cref="MaxSkills"/>.</summary>
    public IReadOnlyList<Skill> Skills { get; }

    public Seniority Seniority { get; }

    public Confidence Confidence { get; }

    // The generated equality would compare the Skills list by reference.
    public bool Equals(Enrichment? other) =>
        other is not null && Seniority == other.Seniority && Confidence == other.Confidence && Skills.SequenceEqual(other.Skills);

    public override int GetHashCode() => HashCode.Combine(Seniority, Confidence, Skills.Count);
}
