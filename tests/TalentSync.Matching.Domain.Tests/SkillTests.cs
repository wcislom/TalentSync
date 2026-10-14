namespace TalentSync.Matching.Domain.Tests;

public sealed class SkillTests
{
    [Fact]
    public void Name_IsTrimmed() => Assert.Equal("C#", new Skill("  C# ").Name);

    [Fact]
    public void Equality_IgnoresCase()
    {
        Assert.Equal(new Skill("Azure"), new Skill("AZURE"));
        Assert.Equal(new Skill("Azure").GetHashCode(), new Skill("AZURE").GetHashCode());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BlankName_Throws(string name) => Assert.Throws<ArgumentException>(() => new Skill(name));

    [Fact]
    public void NameOfMaxLength_IsAllowed() => Assert.Equal(Skill.MaxLength, new Skill(new string('a', Skill.MaxLength)).Name.Length);

    [Fact]
    public void NameLongerThanMaxLength_Throws() =>
        Assert.Throws<ArgumentException>(() => new Skill(new string('a', Skill.MaxLength + 1)));

    [Fact]
    public void Length_IsCheckedAfterTrimming() =>
        Assert.Equal(Skill.MaxLength, new Skill($"  {new string('a', Skill.MaxLength)}  ").Name.Length);
}
