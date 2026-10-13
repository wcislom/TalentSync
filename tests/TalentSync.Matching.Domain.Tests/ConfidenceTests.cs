namespace TalentSync.Matching.Domain.Tests;

public sealed class ConfidenceTests
{
    [Theory]
    [InlineData(Confidence.Min)]
    [InlineData(0.5)]
    [InlineData(Confidence.Max)]
    public void InRange_IsCreated(double value)
    {
        Assert.True(Confidence.TryCreate(value, out var confidence));
        Assert.Equal(value, confidence.Value);
        Assert.Equal(value, new Confidence(value).Value);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void OutOfRange_IsRejected(double value)
    {
        Assert.False(Confidence.TryCreate(value, out var confidence));
        Assert.Null(confidence);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Confidence(value));
    }

    [Fact]
    public void Equality_IsByValue() => Assert.Equal(new Confidence(0.7), new Confidence(0.7));
}
