using System.Collections.Concurrent;

namespace FakeCrm.State;

public enum ContactStatus
{
    Active,
    Deleted,
}

// The dictionary key is the candidate id; the record does not repeat it.
public sealed record Contact(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    ContactStatus Status,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The whole state of the fake CRM. Starts empty: every contact comes from Sync.Worker, so a test can tell
/// "created by TalentSync" from "seeded" (scenario S8 needs a candidate without a contact). Lost on restart.
/// </summary>
public sealed class Store
{
    public ConcurrentDictionary<string, Contact> Contacts { get; } = new();
}
