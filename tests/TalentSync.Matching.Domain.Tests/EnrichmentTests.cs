namespace TalentSync.Matching.Domain.Tests;

public sealed class EnrichmentTests
{
    [Fact]
    public void Skills_AreDeduplicatedCaseInsensitivelyKeepingTheFirst()
    {
        var enrichment = new Enrichment([new Skill("C#"), new Skill("c#"), new Skill("Azure")], Seniority.Senior, new Confidence(0.8));

        Assert.Equal(["C#", "Azure"], enrichment.Skills.Select(skill => skill.Name));
    }

    [Fact]
    public void NoSkills_Throws() =>
        Assert.Throws<ArgumentException>(() => new Enrichment([], Seniority.Junior, new Confidence(0.1)));

    [Fact]
    public void UndefinedSeniority_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Enrichment([new Skill("C#")], (Seniority)42, new Confidence(0.5)));

    [Fact]
    public void Equality_ComparesSkillsByValue() =>
        Assert.Equal(
            new Enrichment([new Skill("C#")], Seniority.Mid, new Confidence(0.5)),
            new Enrichment([new Skill("c#")], Seniority.Mid, new Confidence(0.5)));

    [Fact]
    public void TryCreate_ValidOutput_ReturnsEnrichment()
    {
        var result = Enrichment.TryCreate([" C# ", "c#", "Azure"], "Senior", 0.8);

        var valid = Assert.IsType<EnrichmentValidation.Valid>(result);
        Assert.Equal(new Enrichment([new Skill("C#"), new Skill("Azure")], Seniority.Senior, new Confidence(0.8)), valid.Enrichment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryCreate_BlankSkill_IsInvalid(string? name) =>
        AssertInvalid(EnrichmentRejection.BlankSkill, Enrichment.TryCreate(["C#", name], "Mid", 0.5));

    [Fact]
    public void TryCreate_SkillTooLong_IsInvalid() =>
        AssertInvalid(EnrichmentRejection.SkillTooLong, Enrichment.TryCreate([new string('a', Skill.MaxLength + 1)], "Mid", 0.5));

    [Fact]
    public void TryCreate_NoSkills_IsInvalid()
    {
        AssertInvalid(EnrichmentRejection.NoSkills, Enrichment.TryCreate([], "Mid", 0.5));
        AssertInvalid(EnrichmentRejection.NoSkills, Enrichment.TryCreate(null, "Mid", 0.5));
    }

    [Fact]
    public void TryCreate_ManySkills_AreAllKept()
    {
        var valid = Assert.IsType<EnrichmentValidation.Valid>(Enrichment.TryCreate(Names(100), "Lead", 0.9));

        Assert.Equal(100, valid.Enrichment.Skills.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Expert")]
    [InlineData("senior")]
    [InlineData(" Senior")]
    [InlineData("1")]
    [InlineData("42")]
    public void TryCreate_UnknownSeniority_IsInvalid(string? seniority) =>
        AssertInvalid(EnrichmentRejection.UnknownSeniority, Enrichment.TryCreate(["C#"], seniority, 0.5));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void TryCreate_ConfidenceOutOfRange_IsInvalid(double confidence) =>
        AssertInvalid(EnrichmentRejection.ConfidenceOutOfRange, Enrichment.TryCreate(["C#"], "Mid", confidence));

    private static void AssertInvalid(EnrichmentRejection expected, EnrichmentValidation actual) =>
        Assert.Equal(expected, Assert.IsType<EnrichmentValidation.Invalid>(actual).Reason);

    private static IEnumerable<string> Names(int count) => Enumerable.Range(0, count).Select(i => $"skill-{i}");
}
