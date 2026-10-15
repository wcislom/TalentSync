using TalentSync.Matching.Application.Ports;
using TalentSync.Matching.Domain;
using TalentSync.Matching.Evaluation.Cases;
using TalentSync.Matching.Evaluation.Metrics;

namespace TalentSync.Matching.Evaluation.Tests;

public sealed class MetricCalculatorTests
{
    private const int Precision = 6;

    private static readonly EvaluationCase Case = new(
        "senior-dotnet",
        "purpose",
        "cv",
        new ExpectedEnrichment(
            Required: [["C#"], [".NET", "dotnet"]],
            Acceptable: ["Docker"],
            Forbidden: ["Kubernetes", "Python"],
            Seniority: Seniority.Senior,
            SeniorityTolerance: 1));

    [Fact]
    public void Recall_CountsAnEntryWhenAnyOfItsSpellingsIsReturned()
    {
        var metrics = MetricCalculator.ForCase(Case, [Enriched(["dotnet"])]);

        Assert.Equal(0.5, metrics.Recall!.Value, Precision);
    }

    [Fact]
    public void Skills_AreComparedTrimmedAndCaseInsensitive()
    {
        var metrics = MetricCalculator.ForCase(Case, [Enriched(["  c# ", "DOTNET"])]);

        Assert.Equal(1, metrics.Recall!.Value, Precision);
        Assert.Equal(1, metrics.Precision!.Value, Precision);
    }

    [Fact]
    public void Precision_CountsRequiredSpellingsAndAcceptableSkills()
    {
        var metrics = MetricCalculator.ForCase(Case, [Enriched(["C#", "Docker", "Git", "SQL"])]);

        Assert.Equal(0.5, metrics.Precision!.Value, Precision);
    }

    [Fact]
    public void RecallAndPrecision_AreMeansOverEnrichedRuns()
    {
        var metrics = MetricCalculator.ForCase(Case, [Enriched(["C#", ".NET"]), Enriched(["C#", "Git"])]);

        Assert.Equal(0.75, metrics.Recall!.Value, Precision);
        Assert.Equal(0.75, metrics.Precision!.Value, Precision);
    }

    [Fact]
    public void Recall_WithoutRequiredEntries_IsUndefined()
    {
        var withoutRequired = Case with { Expected = Case.Expected with { Required = [] } };

        Assert.Null(MetricCalculator.ForCase(withoutRequired, [Enriched(["C#"])]).Recall);
    }

    [Fact]
    public void HallucinationRate_IsTheShareOfRunsWithAForbiddenSkill_AndListsThem()
    {
        var metrics = MetricCalculator.ForCase(Case,
            [Enriched(["C#", "kubernetes"]), Enriched(["C#", "Python", "Kubernetes"]), Enriched(["C#"]), Enriched(["C#"])]);

        Assert.Equal(0.5, metrics.HallucinationRate!.Value, Precision);
        Assert.Equal(["kubernetes", "Python"], metrics.ForbiddenReturned);
    }

    [Fact]
    public void Seniority_IsSplitIntoExactNearAndWorse_ByTheOrderOfLevels()
    {
        var metrics = MetricCalculator.ForCase(Case,
        [
            Enriched(["C#"], Seniority.Senior),
            Enriched(["C#"], Seniority.Lead),
            Enriched(["C#"], Seniority.Mid),
            Enriched(["C#"], Seniority.Junior),
        ]);

        Assert.Equal((1, 2, 1), (metrics.SeniorityExact, metrics.SeniorityNear, metrics.SeniorityWorse));
        Assert.Equal(0.75, metrics.SeniorityWithinTolerance!.Value, Precision);
    }

    [Fact]
    public void Seniority_WithZeroTolerance_CountsOnlyTheExactLevel()
    {
        var strict = Case with { Expected = Case.Expected with { SeniorityTolerance = 0 } };

        var metrics = MetricCalculator.ForCase(strict, [Enriched(["C#"], Seniority.Senior), Enriched(["C#"], Seniority.Lead)]);

        Assert.Equal(0.5, metrics.SeniorityWithinTolerance!.Value, Precision);
    }

    [Fact]
    public void Stability_IsTheMeanPairwiseJaccardOfSkillSets()
    {
        // Pairs: {C#,.NET}-{C#,.NET} = 1, {C#,.NET}-{c#,SQL} = 1/3, twice.
        var metrics = MetricCalculator.ForCase(Case, [Enriched(["C#", ".NET"]), Enriched(["C#", ".NET"]), Enriched(["c#", "SQL"])]);

        Assert.Equal((1 + 1 / 3.0 + 1 / 3.0) / 3, metrics.Stability!.Value, Precision);
    }

    [Fact]
    public void Stability_WithFewerThanTwoEnrichedRuns_IsUndefined() =>
        Assert.Null(MetricCalculator.ForCase(Case, [Enriched(["C#"]), Failed(EnrichmentFailure.InvalidOutput)]).Stability);

    [Fact]
    public void Confidence_IsAveragedSeparatelyForExactAndNotExactSeniority()
    {
        var metrics = MetricCalculator.ForCase(Case,
        [
            Enriched(["C#"], Seniority.Senior, 0.9),
            Enriched(["C#"], Seniority.Senior, 0.7),
            Enriched(["C#"], Seniority.Lead, 0.4),
        ]);

        Assert.Equal(0.8, metrics.MeanConfidenceWhenExact!.Value, Precision);
        Assert.Equal(0.4, metrics.MeanConfidenceWhenNotExact!.Value, Precision);
    }

    [Fact]
    public void FailedRuns_LowerTheValidRate_AndAreLeftOutOfQualityMetrics()
    {
        var metrics = MetricCalculator.ForCase(Case,
        [
            Enriched(["C#", ".NET"]),
            Failed(EnrichmentFailure.InvalidOutput),
            Failed(EnrichmentFailure.Timeout),
            Failed(EnrichmentFailure.Unavailable),
        ]);

        Assert.Equal((4, 1, 1, 1, 1), (metrics.Calls, metrics.Enriched, metrics.InvalidOutput, metrics.Timeouts, metrics.Unavailable));
        Assert.Equal(0.25, metrics.ValidRate, Precision);
        Assert.Equal(1, metrics.Recall!.Value, Precision);
        Assert.Equal(2, metrics.InfrastructureFailures);
    }

    [Fact]
    public void NoEnrichedRun_LeavesQualityMetricsUndefined()
    {
        var metrics = MetricCalculator.ForCase(Case, [Failed(EnrichmentFailure.InvalidOutput)]);

        Assert.Equal(0, metrics.ValidRate);
        Assert.Null(metrics.Recall);
        Assert.Null(metrics.Precision);
        Assert.Null(metrics.HallucinationRate);
        Assert.Null(metrics.SeniorityWithinTolerance);
    }

    [Fact]
    public void Aggregate_IsTheMeanOfCases_WithUndefinedValuesLeftOut()
    {
        var withoutRequired = Case with { Id = "no-required", Expected = Case.Expected with { Required = [] } };
        var cases = new[]
        {
            MetricCalculator.ForCase(Case, [Enriched(["C#"])]),                   // recall 0.5, 1 call of 1 valid
            MetricCalculator.ForCase(Case, [Enriched(["C#", ".NET"]), Failed(EnrichmentFailure.InvalidOutput)]), // recall 1, valid 0.5
            MetricCalculator.ForCase(withoutRequired, [Enriched(["C#"])]),        // recall n/a
        };

        var aggregate = MetricCalculator.Aggregate(cases, PassingThresholds());

        Assert.Equal(0.75, Value(aggregate, MetricCalculator.Recall)!.Value, Precision);
        Assert.Equal(2.5 / 3, Value(aggregate, MetricCalculator.ValidRate)!.Value, Precision);
    }

    [Fact]
    public void Aggregate_WithAnInfrastructureFailure_IsIncompleteAndDoesNotPass()
    {
        var cases = new[] { MetricCalculator.ForCase(Case, [Enriched(["C#", ".NET"]), Enriched(["C#", ".NET"]), Failed(EnrichmentFailure.Timeout)]) };

        var aggregate = MetricCalculator.Aggregate(cases, new MetricThresholds { MaxHallucinationRate = 1 });

        Assert.True(aggregate.Incomplete);
        Assert.True(aggregate.Thresholds.All(t => t.Passed));
        Assert.False(aggregate.Passed);
    }

    [Fact]
    public void Aggregate_MetricBelowItsThreshold_DoesNotPass()
    {
        var cases = new[] { MetricCalculator.ForCase(Case, [Enriched(["C#", "Python"]), Enriched(["C#", "Python"])]) };

        var aggregate = MetricCalculator.Aggregate(cases, new MetricThresholds { MaxHallucinationRate = 0.1 });

        Assert.False(aggregate.Thresholds.Single(t => t.Metric == MetricCalculator.HallucinationRate).Passed);
        Assert.False(aggregate.Passed);
    }

    [Fact]
    public void Threshold_UndefinedValue_Fails() =>
        Assert.False(new ThresholdResult(MetricCalculator.Stability, null, 0, ThresholdKind.AtLeast).Passed);

    [Theory]
    [InlineData(0.8, 0.8, ThresholdKind.AtLeast, true)]
    [InlineData(0.7999999999, 0.8, ThresholdKind.AtLeast, true)]
    [InlineData(0.79, 0.8, ThresholdKind.AtLeast, false)]
    [InlineData(0.1, 0.1, ThresholdKind.AtMost, true)]
    [InlineData(0.11, 0.1, ThresholdKind.AtMost, false)]
    public void Threshold_ComparesInItsDirection(double value, double threshold, ThresholdKind kind, bool passed) =>
        Assert.Equal(passed, new ThresholdResult("metric", value, threshold, kind).Passed);

    private static EnrichmentOutcome Enriched(string[] skills, Seniority seniority = Seniority.Senior, double confidence = 0.8) =>
        new EnrichmentOutcome.Enriched(new Enrichment(skills.Select(s => new Skill(s)), seniority, new Confidence(confidence)), "model");

    private static EnrichmentOutcome Failed(EnrichmentFailure reason) => new EnrichmentOutcome.Failed(reason);

    private static MetricThresholds PassingThresholds() => new() { MaxHallucinationRate = 1 };

    private static double? Value(AggregateMetrics aggregate, string metric) => aggregate.Thresholds.Single(t => t.Metric == metric).Value;
}
