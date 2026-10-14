namespace TalentSync.Matching.Domain;

/// <summary>How sure the model is of its suggestion, in [0, 1]. Says nothing about how well a consultant fits a client (D5).</summary>
public sealed record Confidence
{
    public const double Min = 0;

    public const double Max = 1;

    public Confidence(double value)
    {
        if (!IsInRange(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Confidence must be in [{Min}, {Max}].");
        }

        Value = value;
    }

    public double Value { get; }

    // Written so that NaN is out of range too.
    public static bool IsInRange(double value) => value is >= Min and <= Max;
}
