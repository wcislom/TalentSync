using System.Security.Cryptography;
using System.Text;

namespace TalentSync.Matching.Evaluation.Cases;

/// <summary>
/// Identifies the golden set a report was measured on (ADR 009). Shared by the runner and the CI guard, so both
/// compute the same value.
/// </summary>
public static class GoldenSetHash
{
    private const int Length = 16;

    /// <summary>The case files in hash order: top-level <c>*.json</c>, sorted ordinally by file name.</summary>
    public static IReadOnlyList<string> CaseFiles(string directory) =>
        Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

    public static string Compute(string directory) =>
        Compute(CaseFiles(directory).Select(path => (Path.GetFileName(path), File.ReadAllText(path))));

    /// <remarks>
    /// Line endings are normalised to LF (and <see cref="File.ReadAllText(string)"/> drops a BOM): a Windows checkout
    /// with autocrlf and a Linux CI runner must get the same hash from the same commit.
    /// </remarks>
    public static string Compute(IEnumerable<(string FileName, string Content)> files)
    {
        var text = new StringBuilder();
        foreach (var (fileName, content) in files.OrderBy(f => f.FileName, StringComparer.Ordinal))
        {
            // NUL separators: no file name or JSON text contains one, so name and content cannot run into each other.
            text.Append(fileName).Append('\0').Append(content.Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\0');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..Length];
    }
}
