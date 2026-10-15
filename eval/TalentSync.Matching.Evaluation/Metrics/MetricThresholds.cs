namespace TalentSync.Matching.Evaluation.Metrics;

/// <summary>Bound from <c>Evaluation:Thresholds</c>; starting values in ADR 009.</summary>
public sealed class MetricThresholds
{
    public double MinValidRate { get; set; }

    public double MinRecall { get; set; }

    public double MinPrecision { get; set; }

    public double MaxHallucinationRate { get; set; }

    public double MinSeniorityAccepted { get; set; }

    public double MinStability { get; set; }
}
