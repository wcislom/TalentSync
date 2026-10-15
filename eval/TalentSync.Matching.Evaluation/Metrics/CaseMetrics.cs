namespace TalentSync.Matching.Evaluation.Metrics;

/// <summary>Metrics of one case. <c>null</c> = undefined for this case (shown as n/a, left out of the aggregate).</summary>
/// <param name="SeniorityNear">Within tolerance but not exact.</param>
public sealed record CaseMetrics(
    string CaseId,
    int Calls,
    int Enriched,
    int InvalidOutput,
    int Timeouts,
    int Unavailable,
    double ValidRate,
    double? Recall,
    double? Precision,
    double? HallucinationRate,
    IReadOnlyList<string> ForbiddenReturned,
    int SeniorityExact,
    int SeniorityNear,
    int SeniorityWorse,
    double? SeniorityWithinTolerance,
    double? Stability,
    double? MeanConfidenceWhenExact,
    double? MeanConfidenceWhenNotExact)
{
    /// <summary>Calls that never got an answer: any of them makes the report INCOMPLETE.</summary>
    public int InfrastructureFailures => Timeouts + Unavailable;
}
