namespace TalentSync.Matching.Domain.Tests;

public sealed class ConfidenceTests
{
    [Theory]
    [InlineData(Confidence.Min)]
    [InlineData(0.5)]
    [InlineData(Confidence.Max)]
    public void InRange_KeepsTheValue(double value) => Assert.Equal(value, new Confidence(value).Value);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void OutOfRange_Throws(double value) => Assert.Throws<ArgumentOutOfRangeException>(() => new Confidence(value));

    [Fact]
    public void Equality_IsByValue() => Assert.Equal(new Confidence(0.7), new Confidence(0.7));
}
