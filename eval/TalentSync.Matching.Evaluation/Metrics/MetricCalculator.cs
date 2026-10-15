using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;
using TalentSync.Matching.Evaluation.Cases;

namespace TalentSync.Matching.Evaluation.Metrics;

/// <summary>
/// Definitions in ADR 009. Skills are compared as <see cref="Skill"/> values (trimmed, case-insensitive), the same
/// equality the profile uses. All metrics except the valid rate are over enriched runs only.
/// </summary>
public static class MetricCalculator
{
    public const string ValidRate = "Valid rate";
    public const string Recall = "Recall";
    public const string Precision = "Precision";
    public const string HallucinationRate = "Hallucination rate";
    public const string SeniorityAccepted = "Seniority accepted";
    public const string Stability = "Stability";

    public static CaseMetrics ForCase(EvaluationCase evaluationCase, IReadOnlyList<EnrichmentOutcome> runs)
    {
        var expected = evaluationCase.Expected;
        var enriched = runs.OfType<EnrichmentOutcome.Enriched>().Select(r => r.Enrichment).ToList();
        var failures = runs.OfType<EnrichmentOutcome.Failed>().Select(f => f.Reason).ToList();

        var required = expected.Required.Select(entry => entry.Select(s => new Skill(s)).ToHashSet()).ToList();
        var correct = required.SelectMany(entry => entry).Concat(expected.Acceptable.Select(s => new Skill(s))).ToHashSet();
        var forbidden = expected.Forbidden.Select(s => new Skill(s)).ToHashSet();

        var hallucinated = enriched.Select(e => e.Skills.Where(forbidden.Contains).ToList()).ToList();
        var exact = enriched.Count(e => e.Seniority == expected.Seniority);
        var alsoAccepted = enriched.Count(e => expected.SeniorityAlsoAccepted.Contains(e.Seniority));

        return new CaseMetrics(
            evaluationCase.Id,
            Calls: runs.Count,
            Enriched: enriched.Count,
            InvalidOutput: failures.Count(f => f == EnrichmentFailure.InvalidOutput),
            Timeouts: failures.Count(f => f == EnrichmentFailure.Timeout),
            Unavailable: failures.Count(f => f == EnrichmentFailure.Unavailable),
            ValidRate: runs.Count == 0 ? 0 : (double)enriched.Count / runs.Count,
            Recall: required.Count == 0
                ? null
                : Mean(enriched.Select(e => (double)required.Count(entry => e.Skills.Any(entry.Contains)) / required.Count)),
            Precision: Mean(enriched.Select(e => (double)e.Skills.Count(correct.Contains) / e.Skills.Count)),
            HallucinationRate: Rate(hallucinated.Count(h => h.Count > 0), enriched.Count),
            ForbiddenReturned: hallucinated.SelectMany(h => h).Distinct().Select(s => s.Name).Order(StringComparer.OrdinalIgnoreCase).ToList(),
            SeniorityExact: exact,
            SeniorityAlsoAccepted: alsoAccepted,
            SeniorityWrong: enriched.Count - exact - alsoAccepted,
            SeniorityAccepted: Rate(exact + alsoAccepted, enriched.Count),
            Stability: MeanPairwiseJaccard(enriched.Select(e => e.Skills.ToHashSet()).ToList()),
            MeanConfidenceWhenExact: Mean(enriched.Where(e => e.Seniority == expected.Seniority).Select(e => e.Confidence.Value)),
            MeanConfidenceWhenNotExact: Mean(enriched.Where(e => e.Seniority != expected.Seniority).Select(e => e.Confidence.Value)));
    }

    /// <summary>The mean of the per-case values, so every case weighs the same; undefined values are left out.</summary>
    public static AggregateMetrics Aggregate(IReadOnlyList<CaseMetrics> cases, MetricThresholds thresholds) => new(
        [
            new(ValidRate, Mean(cases.Select(c => (double?)c.ValidRate)), thresholds.MinValidRate, ThresholdKind.AtLeast),
            new(Recall, Mean(cases.Select(c => c.Recall)), thresholds.MinRecall, ThresholdKind.AtLeast),
            new(Precision, Mean(cases.Select(c => c.Precision)), thresholds.MinPrecision, ThresholdKind.AtLeast),
            new(HallucinationRate, Mean(cases.Select(c => c.HallucinationRate)), thresholds.MaxHallucinationRate, ThresholdKind.AtMost),
            new(SeniorityAccepted, Mean(cases.Select(c => c.SeniorityAccepted)), thresholds.MinSeniorityAccepted, ThresholdKind.AtLeast),
            new(Stability, Mean(cases.Select(c => c.Stability)), thresholds.MinStability, ThresholdKind.AtLeast),
        ],
        Mean(cases.Select(c => c.MeanConfidenceWhenExact)),
        Mean(cases.Select(c => c.MeanConfidenceWhenNotExact)),
        cases.Sum(c => c.Timeouts),
        cases.Sum(c => c.Unavailable));

    private static double? Rate(int count, int total) => total == 0 ? null : (double)count / total;

    private static double? Mean(IEnumerable<double> values) => Mean(values.Select(v => (double?)v));

    private static double? Mean(IEnumerable<double?> values)
    {
        var defined = values.OfType<double>().ToList();
        return defined.Count == 0 ? null : defined.Average();
    }

    private static double? MeanPairwiseJaccard(IReadOnlyList<HashSet<Skill>> sets)
    {
        var similarities = new List<double>();
        for (var i = 0; i < sets.Count; i++)
        {
            for (var j = i + 1; j < sets.Count; j++)
            {
                var union = sets[i].Union(sets[j]).Count();
                similarities.Add((double)sets[i].Intersect(sets[j]).Count() / union);
            }
        }

        return Mean(similarities);
    }
}
