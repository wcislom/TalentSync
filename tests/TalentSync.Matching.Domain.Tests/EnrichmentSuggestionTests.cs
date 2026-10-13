namespace TalentSync.Matching.Domain.Tests;

public sealed class EnrichmentSuggestionTests
{
    [Fact]
    public void Validate_ValidOutput_TrimsAndDeduplicatesSkillsCaseInsensitively()
    {
        var validation = EnrichmentSuggestion.Validate([" C# ", "c#", "Azure"], "Senior", 0.8);

        var suggestion = Assert.IsType<EnrichmentSuggestionValidation.ValidResult>(validation).Suggestion;
        Assert.Equal([new Skill("C#"), new Skill("Azure")], suggestion.Skills);
        Assert.Equal(Seniority.Senior, suggestion.Seniority);
        Assert.Equal(new Confidence(0.8), suggestion.Confidence);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Validate_ConfidenceOnTheBoundary_IsValid(double confidence) =>
        Assert.IsType<EnrichmentSuggestionValidation.ValidResult>(EnrichmentSuggestion.Validate(["C#"], "Mid", confidence));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Principal")]
    [InlineData("senior")]
    [InlineData("2")]
    [InlineData("Senior, Lead")]
    public void Validate_SeniorityNotInTheList_IsInvalid(string? seniority) =>
        Assert.IsType<EnrichmentSuggestionValidation.InvalidResult>(EnrichmentSuggestion.Validate(["C#"], seniority, 0.5));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Validate_ConfidenceOutsideTheRange_IsInvalid(double confidence) =>
        Assert.IsType<EnrichmentSuggestionValidation.InvalidResult>(EnrichmentSuggestion.Validate(["C#"], "Mid", confidence));

    [Fact]
    public void Validate_BlankSkillName_IsInvalid() =>
        Assert.IsType<EnrichmentSuggestionValidation.InvalidResult>(EnrichmentSuggestion.Validate(["C#", " "], "Mid", 0.5));

    [Fact]
    public void Validate_MissingSkills_IsInvalid() =>
        Assert.IsType<EnrichmentSuggestionValidation.InvalidResult>(EnrichmentSuggestion.Validate(null, "Mid", 0.5));

    [Fact]
    public void Validate_MoreThanMaxSkillsAfterDeduplication_IsInvalid()
    {
        var skills = Enumerable.Range(0, EnrichmentSuggestion.MaxSkills + 1).Select(i => $"skill-{i}").ToList();

        Assert.IsType<EnrichmentSuggestionValidation.InvalidResult>(EnrichmentSuggestion.Validate(skills, "Mid", 0.5));
    }

    [Fact]
    public void Validate_DuplicatesBringingTheCountDownToMax_IsValid()
    {
        var skills = Enumerable.Range(0, EnrichmentSuggestion.MaxSkills).Select(i => $"skill-{i}").Append("SKILL-0").ToList();

        Assert.IsType<EnrichmentSuggestionValidation.ValidResult>(EnrichmentSuggestion.Validate(skills, "Mid", 0.5));
    }
}
