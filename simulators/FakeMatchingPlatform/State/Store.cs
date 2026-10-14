using System.Collections.Concurrent;

namespace FakeMatchingPlatform.State;

public enum Seniority
{
    Junior,
    Mid,
    Senior,
    Lead,
}

public enum EnrichmentStatus
{
    Enriched,
    NotEnriched,
}

// The dictionary key is the candidate id; the record does not repeat it.
public sealed record Profile(
    IReadOnlyList<string> Skills,
    Seniority? Seniority,
    double? Confidence,
    EnrichmentStatus EnrichmentStatus,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The whole state of the fake Matching Platform. Starts empty: every profile comes from Matching.Worker
/// (scenario M2 needs a candidate without one). Lost on restart.
/// </summary>
public sealed class Store
{
    public ConcurrentDictionary<string, Profile> Profiles { get; } = new();
}
