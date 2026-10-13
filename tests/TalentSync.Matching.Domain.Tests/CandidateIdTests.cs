namespace TalentSync.Matching.Domain.Tests;

public sealed class CandidateIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BlankValue_Throws(string value) => Assert.Throws<ArgumentException>(() => new CandidateId(value));

    [Fact]
    public void Equality_IsByValue() => Assert.Equal(new CandidateId("c-1"), new CandidateId("c-1"));
}
