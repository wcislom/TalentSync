namespace TalentSync.Matching.Domain;

/// <summary>
/// A skill as the Matching Platform lists it on a profile. Equal regardless of case, so "C#" and "c#" are one skill
/// on a profile.
/// </summary>
public sealed record Skill
{
    public Skill(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public string Name { get; }

    public bool Equals(Skill? other) => other is not null && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
}
