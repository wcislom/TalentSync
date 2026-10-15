using TalentSync.Matching.Evaluation.Metrics;

namespace TalentSync.Matching.Evaluation;

internal sealed class EvaluationOptions
{
    public const string SectionName = "Evaluation";

    public int RunsPerCase { get; set; }

    /// <summary>Relative to the repository root, or absolute.</summary>
    public string CasesDirectory { get; set; } = string.Empty;

    /// <summary>Relative to the repository root, or absolute.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    public MetricThresholds Thresholds { get; set; } = new();

    public bool IsValid() =>
        RunsPerCase > 0 && !string.IsNullOrWhiteSpace(CasesDirectory) && !string.IsNullOrWhiteSpace(OutputDirectory);
}
