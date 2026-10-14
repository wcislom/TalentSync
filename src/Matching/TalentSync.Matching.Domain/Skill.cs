using System.Diagnostics.CodeAnalysis;

namespace TalentSync.Matching.Domain;

/// <summary>
/// A skill as the Matching Platform lists it on a profile. Equal regardless of case, so "C#" and "c#" are one skill
/// on a profile.
/// </summary>
public sealed record Skill
{
    public const int MaxLength = 50;

    public Skill(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (IsTooLong(name))
        {
            throw new ArgumentException($"Skill name longer than {MaxLength} characters.", nameof(name));
        }

        Name = name.Trim();
    }

    public string Name { get; }

    public static bool IsBlank([NotNullWhen(false)] string? name) => string.IsNullOrWhiteSpace(name);

    public static bool IsTooLong(string name) => name.Trim().Length > MaxLength;

    // Replaces the equality the record would generate, which compares Name case-sensitively. GetHashCode must use
    // the same comparer, or Distinct and dictionaries would treat equal skills as different.
    public bool Equals(Skill? other) => other is not null && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
}
