using System.Security.Cryptography;
using System.Text;

namespace TalentSync.Matching.Application.Ports;

/// <summary>
/// Identifies one version of a candidate's CV, so a stored suggestion is reused only for the CV it was made from.
/// </summary>
/// <remarks>
/// Hashed as fetched, before masking: a change to an email address or phone number alone is a new hash and a new model
/// call. Rare, and accepted (ADR 007).
/// </remarks>
public sealed record CvHash
{
    private CvHash(string value) => Value = value;

    /// <summary>Lowercase hex SHA-256, 64 characters.</summary>
    public string Value { get; }

    public static CvHash Of(string cvText)
    {
        ArgumentNullException.ThrowIfNull(cvText);
        return new CvHash(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cvText))));
    }

    public override string ToString() => Value;
}
