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
    public void NoSkills_IsAllowed() =>
        Assert.Empty(new Enrichment([], Seniority.Junior, new Confidence(0.1)).Skills);

    [Fact]
    public void MoreThanMaxSkills_Throws() =>
        Assert.Throws<ArgumentException>(() => new Enrichment(Skills(Enrichment.MaxSkills + 1), Seniority.Mid, new Confidence(0.5)));

    [Fact]
    public void DuplicatesBringingTheCountDownToMax_AreAllowed()
    {
        var skills = Skills(Enrichment.MaxSkills).Append(new Skill("SKILL-0"));

        Assert.Equal(Enrichment.MaxSkills, new Enrichment(skills, Seniority.Mid, new Confidence(0.5)).Skills.Count);
    }

    [Fact]
    public void UndefinedSeniority_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Enrichment([], (Seniority)42, new Confidence(0.5)));

    [Fact]
    public void Equality_ComparesSkillsByValue() =>
        Assert.Equal(
            new Enrichment([new Skill("C#")], Seniority.Mid, new Confidence(0.5)),
            new Enrichment([new Skill("c#")], Seniority.Mid, new Confidence(0.5)));

    private static IEnumerable<Skill> Skills(int count) => Enumerable.Range(0, count).Select(i => new Skill($"skill-{i}"));
}
