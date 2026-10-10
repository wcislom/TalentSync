using System.Text;

namespace TalentSync.Recruitment.Domain;

/// <summary>
/// Read-only snapshot of a candidate as the recruitment source returns it now (notify-then-fetch).
/// Identity is <see cref="Id"/> only; email and phone are not identity keys (D3).
/// </summary>
public sealed record Candidate(
    string Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? CvText)
{
    // The generated ToString would print every property. Only the id is printed, so logging a candidate
    // cannot leak PII (invariant 14).
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id);
        return true;
    }
}
