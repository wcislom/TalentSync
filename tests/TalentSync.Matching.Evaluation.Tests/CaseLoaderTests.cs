using TalentSync.Matching.Domain;
using TalentSync.Matching.Evaluation.Cases;

namespace TalentSync.Matching.Evaluation.Tests;

public sealed class CaseLoaderTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("cases-");

    [Fact]
    public void ValidFile_IsLoaded_WithCommentsAllowed()
    {
        Write("senior-dotnet.json", """
            {
              "id": "senior-dotnet",
              "purpose": "A typical senior CV.",
              "cv": "Fictional CV.",
              "expected": {
                "required": [["C#"], [".NET", "dotnet"]], // each entry = accepted spellings
                "acceptable": ["Docker"],
                "forbidden": ["Python"],
                "seniority": "Senior",
                "seniorityAlsoAccepted": ["Lead"]
              }
            }
            """);

        var loaded = Assert.Single(CaseLoader.Load(_directory.FullName));

        Assert.Equal("senior-dotnet", loaded.Id);
        Assert.Equal([".NET", "dotnet"], loaded.Expected.Required[1]);
        Assert.Equal(Seniority.Senior, loaded.Expected.Seniority);
        Assert.Equal([Seniority.Lead], loaded.Expected.SeniorityAlsoAccepted);
    }

    [Theory]
    [InlineData("other-name", "Senior", "[[\"C#\"]]", "[]")]
    [InlineData("case", "senior", "[[\"C#\"]]", "[]")]
    [InlineData("case", "Senior", "[[]]", "[]")]
    [InlineData("case", "Senior", "[[\" \"]]", "[]")]
    [InlineData("case", "Senior", "[[\"C#\"]]", "[\"Senior\"]")]
    [InlineData("case", "Senior", "[[\"C#\"]]", "[\"Lead\", \"Lead\"]")]
    [InlineData("case", "Senior", "[[\"C#\"]]", "[\"lead\"]")]
    public void InvalidFile_IsRejected_WithItsFileName(string id, string seniority, string required, string alsoAccepted)
    {
        Write("case.json", $$"""
            { "id": "{{id}}", "purpose": "p", "cv": "cv",
              "expected": { "required": {{required}}, "acceptable": [], "forbidden": [], "seniority": "{{seniority}}", "seniorityAlsoAccepted": {{alsoAccepted}} } }
            """);

        var exception = Assert.Throws<InvalidDataException>(() => CaseLoader.Load(_directory.FullName));
        Assert.StartsWith("case.json:", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private void Write(string fileName, string content) => File.WriteAllText(Path.Combine(_directory.FullName, fileName), content);
}
