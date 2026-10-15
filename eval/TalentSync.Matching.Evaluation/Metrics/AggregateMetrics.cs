namespace TalentSync.Matching.Evaluation.Metrics;

public enum ThresholdKind
{
    AtLeast,
    AtMost,
}

public sealed record ThresholdResult(string Metric, double? Value, double Threshold, ThresholdKind Kind)
{
    // An undefined value fails: a report that could not measure a metric has not shown that it meets it.
    // Rounded so that a value printed as 0.80 never fails a threshold of 0.80 by floating-point noise.
    public bool Passed => Value is { } value && Kind switch
    {
        ThresholdKind.AtLeast => Math.Round(value, 6) >= Threshold,
        ThresholdKind.AtMost => Math.Round(value, 6) <= Threshold,
        _ => throw new InvalidOperationException("Unknown threshold kind."),
    };
}

public sealed record AggregateMetrics(
    IReadOnlyList<ThresholdResult> Thresholds,
    double? MeanConfidenceWhenExact,
    double? MeanConfidenceWhenNotExact,
    int Timeouts,
    int Unavailable)
{
    public bool Incomplete => Timeouts + Unavailable > 0;

    public bool Passed => !Incomplete && Thresholds.All(t => t.Passed);
}
