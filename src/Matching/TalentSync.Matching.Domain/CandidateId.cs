namespace TalentSync.Matching.Domain;

/// <summary>
/// The candidate id from the recruitment system, the identity of a consultant profile (D3). Its own type so it
/// cannot be mixed up with the id of a job application.
/// </summary>
public sealed record CandidateId
{
    public CandidateId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
