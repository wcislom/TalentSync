using System.Text;
using TalentSync.Matching.Evaluation.Cases;

namespace TalentSync.Matching.Evaluation.Tests;

public sealed class GoldenSetHashTests
{
    [Fact]
    public void Hash_Is16LowercaseHexCharacters() =>
        Assert.Matches("^[0-9a-f]{16}$", GoldenSetHash.Compute([("a.json", "{}")]));

    [Fact]
    public void FileOrder_DoesNotChangeTheHash() =>
        Assert.Equal(
            GoldenSetHash.Compute([("a.json", "{\"id\":1}"), ("b.json", "{\"id\":2}")]),
            GoldenSetHash.Compute([("b.json", "{\"id\":2}"), ("a.json", "{\"id\":1}")]));

    [Fact]
    public void CrlfAndLf_GiveTheSameHash() =>
        Assert.Equal(
            GoldenSetHash.Compute([("a.json", "{\r\n  \"id\": 1\r\n}\r\n")]),
            GoldenSetHash.Compute([("a.json", "{\n  \"id\": 1\n}\n")]));

    [Fact]
    public void ChangedContent_ChangesTheHash() =>
        Assert.NotEqual(
            GoldenSetHash.Compute([("a.json", "{\"id\":1}")]),
            GoldenSetHash.Compute([("a.json", "{\"id\":2}")]));

    [Fact]
    public void RenamedFile_ChangesTheHash() =>
        Assert.NotEqual(
            GoldenSetHash.Compute([("a.json", "{}")]),
            GoldenSetHash.Compute([("b.json", "{}")]));

    [Fact]
    public void Directory_HashesTopLevelJsonFiles_WithoutBom()
    {
        var directory = Directory.CreateTempSubdirectory("golden-set-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "a.json"), "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.WriteAllText(Path.Combine(directory.FullName, "notes.md"), "not a case");
            Directory.CreateDirectory(Path.Combine(directory.FullName, "nested"));
            File.WriteAllText(Path.Combine(directory.FullName, "nested", "b.json"), "{}");

            Assert.Equal(GoldenSetHash.Compute([("a.json", "{}")]), GoldenSetHash.Compute(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
