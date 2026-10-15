namespace TalentSync.Matching.Evaluation;

/// <summary>The directory with <c>TalentSync.slnx</c>, so paths work from any working directory and from a test's <c>bin/</c>.</summary>
public static class RepositoryRoot
{
    private const string SolutionFile = "TalentSync.slnx";

    public static string Find() =>
        FindFrom(Directory.GetCurrentDirectory()) ?? FindFrom(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException($"No {SolutionFile} found above the working directory or the application directory.");

    public static string Resolve(string path) => Path.GetFullPath(Path.Combine(Find(), path));

    private static string? FindFrom(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
